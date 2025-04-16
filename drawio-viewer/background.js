chrome.webRequest.onBeforeRequest.addListener(
  function(details) {
    if (details.url.endsWith('.drawio') || details.url.endsWith('.drawio.xml')) {
      return {
        redirectUrl: chrome.runtime.getURL('drawio-viewer.html') + '?url=' + encodeURIComponent(details.url)
      };
    }
  },
  { urls: ["https://*.sharepoint.com/*"] },
  ["blocking"]
); 