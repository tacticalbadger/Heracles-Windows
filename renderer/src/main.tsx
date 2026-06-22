import React from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import { ErrorBoundary } from './ErrorBoundary';
import 'tippy.js/dist/tippy.css';
import './styles.css';

const container = document.getElementById('root');

if (!container) {
  throw new Error('Root element not found');
}

// Prevent the window from navigating away (loading the file) when an image or
// other file is dropped outside the editor. The editor's own handleDrop still
// handles drops onto note content.
window.addEventListener('dragover', (event) => {
  if (event.dataTransfer?.types.includes('Files')) {
    event.preventDefault();
  }
});
window.addEventListener('drop', (event) => {
  if (event.dataTransfer?.types.includes('Files')) {
    event.preventDefault();
  }
});

createRoot(container).render(
  <React.StrictMode>
    <ErrorBoundary>
      <App />
    </ErrorBoundary>
  </React.StrictMode>
);
