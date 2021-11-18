import * as rpc from "vscode-jsonrpc/node";
import { Channel } from "..";

export function startJsonRpc(channel: Channel): rpc.MessageConnection {
    return rpc.createMessageConnection(channel.stream, channel.stream);
}
