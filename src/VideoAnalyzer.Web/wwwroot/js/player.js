// Moves a <video> element to a position (seconds) and starts playing, used by "jump to" buttons.
export function seek(video, seconds) {
    if (!video) return;
    video.currentTime = seconds;
    video.scrollIntoView({ behavior: 'smooth', block: 'center' });
    video.play().catch(() => { /* autoplay may be blocked; the position is still set */ });
}
