# Draw.io SharePoint Viewer Chrome Extension

This Chrome extension allows you to view draw.io files directly in your browser when accessing them from SharePoint. Instead of downloading the files, they will open in an embedded draw.io editor.

## Features

- Automatically detects and opens .drawio and .drawio.xml files from SharePoint
- Visual indicators for draw.io files in SharePoint interface
- Full-screen editor view
- Uses the official draw.io editor
- Maintains file integrity

## Installation Instructions

### Method 1: Using Chrome's Developer Mode (Recommended)
1. Download or clone this repository to your local machine
2. Open Google Chrome
3. Go to `chrome://extensions/` in your browser
4. Enable "Developer mode" by toggling the switch in the top-right corner
5. Click "Load unpacked" button
6. Select the `drawio-viewer` folder containing the extension files
7. The extension should now be installed and active

### Method 2: Direct Installation (Advanced)
1. Close Chrome completely
2. Navigate to Chrome's extension directory:
   - Windows: `%LOCALAPPDATA%\Google\Chrome\User Data\Default\Extensions`
   - macOS: `~/Library/Application Support/Google/Chrome/Default/Extensions`
   - Linux: `~/.config/google-chrome/Default/Extensions`
3. Create a new folder with a unique ID (e.g., a UUID)
4. Copy all extension files into this folder
5. Create a `_metadata` folder inside the extension folder
6. Open Chrome and the extension should be automatically installed

Note: Method 2 requires Chrome to be completely closed during the process and may require additional permissions depending on your system configuration.

## Usage

1. Navigate to your SharePoint site
2. Click on any .drawio or .drawio.xml file
3. The file will automatically open in the embedded draw.io editor
4. Draw.io files will be highlighted with a green border in the SharePoint interface

## File Structure

```
drawio-viewer/
├── manifest.json    # Extension configuration
├── background.js    # Handles file interception
├── drawio-viewer.html  # Embedded editor interface
└── content.js      # Adds visual indicators
```

## Permissions

This extension requires the following permissions:
- Access to SharePoint domains
- Web request interception
- Ability to inject content scripts

These permissions are necessary for the extension to:
- Detect draw.io files in SharePoint
- Intercept file downloads
- Display visual indicators
- Open files in the embedded editor

## Troubleshooting

If the extension is not working:
1. Ensure you're accessing a SharePoint site
2. Check that the file extension is .drawio or .drawio.xml
3. Verify the extension is enabled in chrome://extensions/
4. Try reloading the SharePoint page
5. Check the browser console for any error messages

## Security

This extension:
- Only works on SharePoint domains
- Uses the official draw.io editor
- Does not store or transmit your files
- Requires minimal permissions

## Support

For issues or feature requests, please open an issue in the repository. 