import { Component, type ErrorInfo, type ReactNode } from 'react';

type ErrorBoundaryProps = {
  children: ReactNode;
};

type ErrorBoundaryState = {
  error: Error | null;
  info: ErrorInfo | null;
};

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null, info: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error, info: null };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('[ErrorBoundary] caught render error', error, info);
    this.setState({ error, info });
  }

  private handleReset = () => {
    this.setState({ error: null, info: null });
  };

  private handleReload = () => {
    window.location.reload();
  };

  render() {
    const { error, info } = this.state;

    if (!error) {
      return this.props.children;
    }

    return (
      <div className="error-boundary">
        <div className="error-boundary__panel">
          <h1 className="error-boundary__title">Something went wrong</h1>
          <p className="error-boundary__subtitle">
            A render error broke the UI. Recent state is preserved &mdash; click <strong>Try again</strong>
            to retry rendering or <strong>Reload</strong> to restart with a clean slate.
          </p>
          <div className="error-boundary__actions">
            <button type="button" className="error-boundary__btn" onClick={this.handleReset}>
              Try again
            </button>
            <button
              type="button"
              className="error-boundary__btn error-boundary__btn--ghost"
              onClick={this.handleReload}
            >
              Reload
            </button>
          </div>
          <details className="error-boundary__details" open>
            <summary>Error details</summary>
            <pre className="error-boundary__pre">{error.name}: {error.message}</pre>
            {error.stack && <pre className="error-boundary__pre">{error.stack}</pre>}
            {info?.componentStack && (
              <pre className="error-boundary__pre">{info.componentStack}</pre>
            )}
          </details>
        </div>
      </div>
    );
  }
}
