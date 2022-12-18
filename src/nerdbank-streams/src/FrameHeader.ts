import { ControlCode } from './ControlCode'
import { requireInteger } from './Utilities'
import { QualifiedChannelId } from './QualifiedChannelId'

export class FrameHeader {
	private _channel?: QualifiedChannelId

	/**
	 * Initializes a new instance of the `FrameHeader` class.
	 * @param code The kind of frame this is.
	 * @param channel The channel that this frame refers to or carries a payload for.
	 */
	constructor(public readonly code: ControlCode, channel?: QualifiedChannelId) {
		if (channel) {
			requireInteger('channelId', channel.id, 4, 'signed')
		}

		this.code = code
		this._channel = channel
	}

	/** The channel that this frame refers to or carries a payload for. */
	get channel(): QualifiedChannelId | undefined {
		return this._channel
	}

	/**
	 * Gets the channel that this frame refers to or carries a payload for.
	 */
	public get requiredChannel(): QualifiedChannelId {
		if (this.channel) {
			return this.channel
		}

		throw new Error('Expected channel not present in frame header.')
	}

	/** Changes the channel.source property to reflect the opposite perspective (i.e. remote and local values switch). */
	public flipChannelPerspective() {
		if (this._channel) {
			this._channel = { id: this._channel.id, source: this._channel.source * -1 }
		}
	}
}
