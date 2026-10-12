// Every client script reads window.JuxCore and the tab scripts window.JuxUI; in the browser both
// are injected before them (TransformationPatches.ClientAssets), here they are loaded before each
// test file.
import '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-core.js';
import '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-ui.js';
