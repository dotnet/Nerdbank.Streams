// WriteError is a class that is used to store information related to ContentWritingError.
// It is used by both the sending and receiving streams to transmit errors encountered while
// writing content. 
export class WriteError {

    private _errorMessage: string;
    
    constructor(errorMsg: string) {
        this._errorMessage = errorMsg;
    }

    // Returns the error message associated with this error
    getErrorMessage() : string {
        return this._errorMessage;
    }
}