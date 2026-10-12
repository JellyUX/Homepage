// Every client script reads window.JuxCore at call time; in the browser jux-core.js is injected
// first (TransformationPatches.ClientAssets), here it is loaded before each test file.
import '../src/Jellyfin.Plugin.JuxHomepage/Web/jux-core.js';
