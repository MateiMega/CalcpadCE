const typescript = require('@rollup/plugin-typescript');
const commonjs = require('@rollup/plugin-commonjs');
const resolve = require('@rollup/plugin-node-resolve');
const json = require('@rollup/plugin-json');

// Async so the ESM version helper can be imported: the version the webview is built with.
module.exports = async () => ({
  input: 'src/extension.ts',
  output: {
    file: 'dist/extension.js',
    format: 'cjs',
    sourcemap: true,
    exports: 'auto',
    intro: `const __CALCPAD_VERSION__ = ${JSON.stringify((await import('../../../tools/version.mjs')).resolveVersion())};`
  },
  external: ['vscode'],
  plugins: [
    json(),
    resolve({
      preferBuiltins: true
    }),
    commonjs(),
    typescript({
      tsconfig: './tsconfig.json',
      compilerOptions: {
        module: 'esnext',
        outDir: null
      },
      sourceMap: true,
      inlineSources: false
    })
  ]
});
