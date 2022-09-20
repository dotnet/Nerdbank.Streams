/**
 * A class that is used to store information related to ContentWritingError.
 * It is used by both the sending and receiving streams to transmit errors encountered while
 * writing content.
 */
export class WriteError {
    /**
     * Initializes a new instance of the WriteError class.
     * @param message The error message.
     */
    constructor(public readonly message: string) {
    }
}
