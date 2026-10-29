import {
  HubConnectionBuilder,
  HttpTransportType,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";
import { MessagePackHubProtocol } from "@microsoft/signalr-protocol-msgpack";

let connection: HubConnection | null = null;

/**
 * Connects to /hubs/realtime with MessagePack over WebSockets. The cookie
 * session is sent with the same-origin request, so no token handling is needed.
 */
export function connectRealtime(): Promise<HubConnection> {
  if (connection) {
    return Promise.resolve(connection);
  }

  const hub = new HubConnectionBuilder()
    .withUrl("/hubs/realtime", { transport: HttpTransportType.WebSockets })
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect([0, 2_000, 5_000, 10_000])
    .configureLogging(LogLevel.Warning)
    .build();

  const root = document.documentElement;

  hub.onreconnecting(() => root.classList.remove("is-realtime-connected"));
  hub.onreconnected(() => root.classList.add("is-realtime-connected"));
  hub.onclose(() => root.classList.remove("is-realtime-connected"));

  hub.on("pong", (timestamp: unknown) => {
    root.dataset.realtimePong = String(timestamp);
  });

  return hub.start().then(async () => {
    connection = hub;
    root.classList.add("is-realtime-connected");
    await hub.invoke("Ping");
    return hub;
  });
}
