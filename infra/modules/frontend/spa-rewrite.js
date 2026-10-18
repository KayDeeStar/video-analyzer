// CloudFront viewer-request function for the Blazor single-page app.
// Requests whose last path segment has no file extension (e.g. /jobs/20261018-...)
// are client-side routes, so serve index.html and let the Blazor router handle them.
function handler(event) {
    var request = event.request;
    var lastSegment = request.uri.split('/').pop();
    if (lastSegment.indexOf('.') === -1) {
        request.uri = '/index.html';
    }
    return request;
}
