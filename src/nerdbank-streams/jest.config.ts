/* eslint-disable @typescript-eslint/naming-convention */
import type { Config } from 'jest'

const config: Config = {
	transform: { '\\.ts$': 'ts-jest' },
	testEnvironment: 'node',
	testRegex: 'src/tests/.*\\.spec\\.ts',
}

export default config
