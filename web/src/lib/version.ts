/// The app's version, e.g. `0.0.0` locally or `0.0.0.42` from a CI build. Baked in at build
/// time from the repo's Directory.Build.props (vite.config.ts) — the single place the version
/// lives, shared with the .NET assemblies. The only file that touches the raw constant.
export const appVersion: string = __APP_VERSION__
