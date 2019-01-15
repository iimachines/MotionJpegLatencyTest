onmessage = function (e) {
    const [frameId, buffer, byteOffset, length] = e.data;
    const imageView = new Uint8Array(buffer, byteOffset, length);
    const imageBlob = new Blob([imageView], { type: "image/jpeg" });
    createImageBitmap(imageBlob)
        .then(imageBitmap => {
            self.postMessage([imageBitmap, frameId], [imageBitmap]);
        });
};




