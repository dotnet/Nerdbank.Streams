const typescriptEslint = require('@typescript-eslint/eslint-plugin')
const prettier = require('eslint-plugin-prettier')
const tsParser = require('@typescript-eslint/parser')
const prettierConfig = require('eslint-config-prettier')

module.exports = [
	{
		files: ['**/*.ts'],
		languageOptions: {
			parser: tsParser,
			parserOptions: {
				ecmaVersion: 6,
				sourceType: 'module',
			},
		},
		plugins: {
			'@typescript-eslint': typescriptEslint,
			prettier: prettier,
		},
		rules: {
			'@typescript-eslint/semi': 'off',
			curly: 'warn',
			'object-curly-spacing': 'off',
			eqeqeq: 'warn',
			'no-throw-literal': 'error',
			'no-unexpected-multiline': 'error',
			semi: 'off',
			'prettier/prettier': [
				'error',
				{
					printWidth: 160,
					semi: false,
					singleQuote: true,
					trailingComma: 'es5',
					useTabs: true,
					endOfLine: 'auto',
					arrowParens: 'avoid',
				},
			],
			...prettierConfig.rules,
		},
	},
	{
		ignores: ['out/**', 'dist/**', '**/*.d.ts'],
	},
]
