const path = require('path');
const { fileURLToPath } = require('url');

const DEV_ORIGIN = 'http://localhost:5173';

// Decides which URLs a window may display. The app is a single document: the
// Vite dev server in development, the bundled index.html once packaged (hash
// and query ignored). Kept free of Electron imports so it can be tested.
const createNavigationPolicy = ({ isPackaged, indexPath }) => {
  const appIndexPath = path.resolve(indexPath);
  const samePath = (a, b) =>
    process.platform === 'win32' ? a.toLowerCase() === b.toLowerCase() : a === b;

  const isAppUrl = (url) => {
    try {
      const parsed = new URL(url);
      if (!isPackaged) return parsed.origin === DEV_ORIGIN;
      if (parsed.protocol !== 'file:') return false;
      return samePath(path.resolve(fileURLToPath(parsed)), appIndexPath);
    } catch {
      return false;
    }
  };

  const isExternalUrl = (url) => {
    try {
      const { protocol } = new URL(url);
      return protocol === 'http:' || protocol === 'https:';
    } catch {
      return false;
    }
  };

  return { isAppUrl, isExternalUrl };
};

module.exports = { createNavigationPolicy };
