// Add visual indicators for draw.io files
function addDrawioIndicators() {
    const links = document.querySelectorAll('a[href$=".drawio"], a[href$=".drawio.xml"]');
    links.forEach(link => {
        link.style.border = '2px solid #4CAF50';
        link.style.padding = '2px 5px';
        link.style.borderRadius = '3px';
    });
}

// Run when the page loads
addDrawioIndicators();

// Also run when new content is loaded (for dynamic pages)
const observer = new MutationObserver(addDrawioIndicators);
observer.observe(document.body, {
    childList: true,
    subtree: true
}); 