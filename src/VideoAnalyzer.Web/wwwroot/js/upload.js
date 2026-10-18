// Uploads the file selected in an <input type="file"> straight to S3 using pre-signed
// multipart part URLs. Runs in JavaScript so the video bytes never pass through .NET/WASM memory.

const activeUploads = new Map();

/**
 * @param {HTMLInputElement} input   the file input (InputFile.Element)
 * @param {string} uploadKey         id used to cancel this upload (the job id)
 * @param {number[]} partNumbers     1-based part numbers
 * @param {string[]} urls            pre-signed PUT URL for each part (same order)
 * @param {number} partSize          bytes per part (last part may be smaller)
 * @param {object} dotnetRef         DotNetObjectReference with [JSInvokable] OnUploadProgress(long)
 * @returns {Promise<string[]>}      ETag of each part (same order as partNumbers)
 */
export async function uploadParts(input, uploadKey, partNumbers, urls, partSize, dotnetRef, concurrency = 4) {
    const file = input.files[0];
    if (!file) throw new Error('No file selected.');

    const state = { cancelled: false, requests: new Set() };
    activeUploads.set(uploadKey, state);

    const loadedPerPart = new Array(partNumbers.length).fill(0);
    let lastReport = 0;
    const report = (force) => {
        const now = Date.now();
        if (!force && now - lastReport < 250) return;
        lastReport = now;
        const loaded = loadedPerPart.reduce((a, b) => a + b, 0);
        dotnetRef.invokeMethodAsync('OnUploadProgress', loaded);
    };

    const etags = new Array(partNumbers.length);
    let next = 0;

    async function worker() {
        while (!state.cancelled && next < partNumbers.length) {
            const index = next++;
            const start = (partNumbers[index] - 1) * partSize;
            const blob = file.slice(start, Math.min(start + partSize, file.size));
            etags[index] = await putWithRetry(urls[index], blob, state, (loaded) => {
                loadedPerPart[index] = loaded;
                report(false);
            });
            loadedPerPart[index] = blob.size;
            report(false);
        }
    }

    try {
        const workers = Math.min(concurrency, partNumbers.length);
        await Promise.all(Array.from({ length: workers }, worker));
    } finally {
        activeUploads.delete(uploadKey);
    }

    if (state.cancelled) throw new Error('Upload cancelled.');
    report(true);
    return etags;
}

export function cancelUpload(uploadKey) {
    const state = activeUploads.get(uploadKey);
    if (!state) return;
    state.cancelled = true;
    state.requests.forEach((xhr) => xhr.abort());
}

async function putWithRetry(url, blob, state, onProgress, attempts = 3) {
    for (let attempt = 1; ; attempt++) {
        try {
            return await put(url, blob, state, onProgress);
        } catch (err) {
            if (state.cancelled || attempt >= attempts) throw err;
            onProgress(0);
            await new Promise((resolve) => setTimeout(resolve, 1000 * attempt));
        }
    }
}

// XMLHttpRequest (not fetch) because it reports upload progress.
function put(url, blob, state, onProgress) {
    return new Promise((resolve, reject) => {
        const xhr = new XMLHttpRequest();
        state.requests.add(xhr);
        const done = () => state.requests.delete(xhr);

        xhr.open('PUT', url);
        xhr.upload.onprogress = (e) => onProgress(e.loaded);
        xhr.onload = () => {
            done();
            const etag = xhr.getResponseHeader('ETag');
            if (xhr.status >= 200 && xhr.status < 300 && etag) resolve(etag);
            else reject(new Error(`S3 rejected part (HTTP ${xhr.status}).`));
        };
        xhr.onerror = () => { done(); reject(new Error('Network error while uploading.')); };
        xhr.onabort = () => { done(); reject(new Error('Upload cancelled.')); };
        xhr.send(blob);
    });
}
