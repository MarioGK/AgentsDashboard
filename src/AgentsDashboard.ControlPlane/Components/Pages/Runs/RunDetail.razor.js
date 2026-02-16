window.agentsDashboard = window.agentsDashboard || {};

window.agentsDashboard.downloadBase64File = (fileName, contentType, base64Payload) => {
    const anchor = document.createElement("a");
    anchor.href = `data:${contentType};base64,${base64Payload}`;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
};
