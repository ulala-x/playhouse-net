# PlayHouse JavaScript Connector

TypeScript/JavaScript Connector for PlayHouse real-time game server framework.

## Overview

- **Purpose**: Web browser + Node.js E2E testing
- **Status**: Planned
- **Language**: TypeScript 5.x (strict mode)

## Directory Structure

```
connectors/javascript/
├── src/
│   ├── index.ts                # Entry point
│   ├── connector.ts            # Main API
│   ├── packet.ts               # Packet abstraction
│   ├── config.ts               # Configuration
│   ├── internal/
│   │   ├── client-network.ts   # Networking core
│   │   ├── tcp-connection.ts   # Node.js TCP (net module)
│   │   ├── ws-connection.ts    # Browser WebSocket
│   │   └── packet-codec.ts     # Encode/decode
│   └── types.ts
├── tests/
│   ├── unit/
│   └── e2e/
├── package.json
├── tsconfig.json
├── rollup.config.js            # ESM/CJS/UMD build
└── vitest.config.ts
```

## Technology Stack

| Component | Technology |
|-----------|------------|
| Language | TypeScript 5.x |
| Runtime | Node.js 18+ / Browser (ES2020+) |
| Build | Rollup (tree-shaking, multi-format) |
| Testing | Vitest + Playwright (E2E) |
| Package | npm / CDN (unpkg, jsdelivr) |

## Output Formats

| Format | File | Target |
|--------|------|--------|
| ESM | dist/esm/index.js | Modern bundlers |
| CJS | dist/cjs/index.cjs | Node.js require |
| UMD | dist/umd/playhouse.min.js | Browser CDN |
| Types | dist/types/index.d.ts | TypeScript |

## API Design

```typescript
export interface ConnectorConfig {
    sendBufferSize?: number;      // Default: 65536 (64KB)
    receiveBufferSize?: number;   // Default: 262144 (256KB)
    heartbeatIntervalMs?: number; // Default: 10000 (10s)
    requestTimeoutMs?: number;    // Default: 30000 (30s)
    enableReconnect?: boolean;    // Default: false
    reconnectIntervalMs?: number; // Default: 5000
}

export class Connector {
    // Lifecycle
    init(config?: ConnectorConfig): void;
    connect(host: string, port: number): Promise<void>;
    disconnect(): void;
    get isConnected(): boolean;

    // Messaging
    send(packet: Packet): void;
    request(packet: Packet): Promise<Packet>;

    // Authentication
    authenticate(serviceId: string, accountId: string, payload?: Uint8Array): Promise<boolean>;

    // Browser integration
    mainThreadAction(): void;  // Use with requestAnimationFrame

    // Callbacks
    onConnect?: () => void;
    onReceive?: (packet: Packet) => void;
    onError?: (code: number, message: string) => void;
    onDisconnect?: () => void;
}
```

### Packet

```typescript
export class Packet {
    readonly msgId: string;
    readonly msgSeq: number;
    readonly stageId: bigint;
    readonly errorCode: number;
    readonly payload: Uint8Array;

    // Factory methods
    static empty(msgId: string): Packet;
    static fromProto<T>(proto: T): Packet;  // protobufjs Message
    static fromBytes(msgId: string, bytes: Uint8Array): Packet;

    // Protobuf deserialization
    parse<T>(decoder: { decode: (data: Uint8Array) => T }): T;

    // Resource cleanup
    dispose(): void;
}
```

## Protocol Format

### Request Packet
```
┌─────────────┬────────────┬─────────┬─────────┬─────────┬─────────┐
│ ContentSize │ MsgIdLen   │ MsgId   │ MsgSeq  │ StageId │ Payload │
│ (4 bytes)   │ (1 byte)   │ (N)     │ (2)     │ (8)     │ (...)   │
└─────────────┴────────────┴─────────┴─────────┴─────────┴─────────┘
```

### Response Packet
```
┌─────────────┬────────────┬─────────┬─────────┬─────────┬───────────┬──────────────┬─────────┐
│ ContentSize │ MsgIdLen   │ MsgId   │ MsgSeq  │ StageId │ ErrorCode │ OriginalSize │ Payload │
│ (4 bytes)   │ (1 byte)   │ (N)     │ (2)     │ (8)     │ (2)       │ (4)          │ (...)   │
└─────────────┴────────────┴─────────┴─────────┴─────────┴───────────┴──────────────┴─────────┘
```

- **Byte Order**: Little-endian (DataView with littleEndian=true)
- **String Encoding**: UTF-8 (TextEncoder/TextDecoder)
- **MsgSeq**: 0 = Push, >0 = Request-Response
- **OriginalSize**: >0 indicates LZ4 compressed

## Platform-Specific Transport

| Platform | Transport | Notes |
|----------|-----------|-------|
| Node.js | net.Socket (TCP) | Full binary protocol support |
| Browser | WebSocket | Requires server-side WebSocket gateway |

### Browser Limitations

- TCP sockets not available in browsers
- WebSocket requires server-side gateway that:
  - Accepts WebSocket connections
  - Bridges to PlayHouse TCP protocol
  - Handles binary frame encoding

### WebSocket Gateway Protocol

The gateway converts between WebSocket frames and PlayHouse TCP protocol:

```
Browser (WebSocket)          Gateway             PlayHouse Server (TCP)
       |                        |                        |
       |---[WS Binary Frame]--->|                        |
       |                        |---[TCP Packet]-------->|
       |                        |<--[TCP Packet]---------|
       |<--[WS Binary Frame]----|                        |
```

**Frame Conversion Rules**:
- Each WebSocket binary message = One complete PlayHouse packet
- Gateway handles TCP stream reassembly (ContentSize framing)
- Gateway preserves binary payload without modification
- WebSocket close = TCP disconnect

**Gateway Implementation Notes**:
- Use `ws` (Node.js) or native WebSocket API
- Handle binary message type (`opcode: 0x02`)
- Implement connection pooling for multiple clients

## Installation

### npm

```bash
npm install @playhouse/connector
```

### CDN

```html
<!-- unpkg -->
<script src="https://unpkg.com/@playhouse/connector/dist/umd/playhouse.min.js"></script>

<!-- jsdelivr -->
<script src="https://cdn.jsdelivr.net/npm/@playhouse/connector/dist/umd/playhouse.min.js"></script>
```

## Usage Examples

### Node.js (ESM)

```typescript
import { Connector, Packet } from '@playhouse/connector';

const connector = new Connector();
connector.init({
    heartbeatIntervalMs: 5000,
    requestTimeoutMs: 10000
});

connector.onConnect = () => console.log('Connected!');
connector.onReceive = (packet) => console.log('Received:', packet.msgId);
connector.onError = (code, msg) => console.error(`Error ${code}: ${msg}`);
connector.onDisconnect = () => console.log('Disconnected');

// Connect
await connector.connect('localhost', 34001);

// Authenticate
await connector.authenticate('game', 'user123');

// Send request
const request = Packet.fromProto(EchoRequest.create({ content: 'Hello' }));
const response = await connector.request(request);
console.log('Response:', response.msgId);

// Cleanup
connector.disconnect();
```

### Node.js (CommonJS)

```javascript
const { Connector, Packet } = require('@playhouse/connector');

const connector = new Connector();
connector.init();

connector.connect('localhost', 34001)
    .then(() => connector.authenticate('game', 'user123'))
    .then(() => {
        const request = Packet.empty('Ping');
        return connector.request(request);
    })
    .then(response => {
        console.log('Pong:', response.msgId);
        connector.disconnect();
    });
```

### Browser

```html
<!DOCTYPE html>
<html>
<head>
    <script src="https://unpkg.com/@playhouse/connector/dist/umd/playhouse.min.js"></script>
</head>
<body>
    <script>
        const { Connector, Packet } = PlayHouse;

        const connector = new Connector();
        connector.init();

        connector.onConnect = () => console.log('Connected!');
        connector.onReceive = (packet) => console.log('Push:', packet.msgId);

        // Connect via WebSocket gateway
        connector.connect('ws://localhost:8080', 0)
            .then(() => connector.authenticate('game', 'user123'))
            .then(() => {
                console.log('Authenticated!');
            });

        // Game loop integration
        function gameLoop() {
            connector.mainThreadAction();
            requestAnimationFrame(gameLoop);
        }
        requestAnimationFrame(gameLoop);
    </script>
</body>
</html>
```

## Build Instructions

### Development

```bash
# Install dependencies
npm install

# Build
npm run build

# Test
npm test

# Watch mode
npm run dev
```

### package.json

```json
{
  "name": "@playhouse/connector",
  "version": "1.0.0",
  "description": "PlayHouse real-time game server connector",
  "main": "dist/cjs/index.cjs",
  "module": "dist/esm/index.js",
  "browser": "dist/umd/playhouse.min.js",
  "types": "dist/types/index.d.ts",
  "exports": {
    ".": {
      "import": "./dist/esm/index.js",
      "require": "./dist/cjs/index.cjs",
      "types": "./dist/types/index.d.ts"
    }
  },
  "files": ["dist"],
  "scripts": {
    "build": "rollup -c",
    "dev": "rollup -c -w",
    "test": "vitest",
    "test:e2e": "playwright test",
    "prepublishOnly": "npm run build && npm test"
  },
  "devDependencies": {
    "@rollup/plugin-node-resolve": "^15.2.3",
    "@rollup/plugin-typescript": "^11.1.5",
    "rollup": "^4.9.0",
    "rollup-plugin-terser": "^7.0.2",
    "typescript": "^5.3.0",
    "vitest": "^1.1.0",
    "@playwright/test": "^1.40.0"
  },
  "dependencies": {
    "protobufjs": "^7.2.5",
    "lz4js": "^0.2.0"
  }
}
```

## Development Tasks

| Phase | Tasks |
|-------|-------|
| Core | Project setup, TypeScript structure, packet codec |
| Transport | Node.js TCP, browser WebSocket transport |
| Reliability | Request-response, heartbeat, error handling |
| Testing | Unit tests, E2E tests |
| Release | npm publish, CDN setup, documentation |

## Error Codes

| Code | Description |
|------|-------------|
| 0 | Success |
| 1001 | Connection failed |
| 1002 | Connection timeout |
| 1003 | Connection closed |
| 2001 | Request timeout |
| 2002 | Invalid response |
| 3001 | Authentication failed |

## Browser Compatibility

| Browser | Version | Notes |
|---------|---------|-------|
| Chrome | 80+ | Full support |
| Firefox | 78+ | Full support |
| Safari | 14+ | Full support |
| Edge | 80+ | Full support |

### BigInt (stageId) Compatibility

The `stageId` field uses `bigint` for 64-bit integer support:

```typescript
// stageId is bigint
const stageId: bigint = packet.stageId;

// Convert to string for JSON serialization
JSON.stringify({ stageId: stageId.toString() });

// Parse from string
const parsed = BigInt("12345678901234567890");
```

**Browser Support**:
- Chrome 67+, Firefox 68+, Safari 14+, Edge 79+
- IE11: Not supported (use polyfill or transpile)

**Polyfill for older browsers**:
```bash
npm install jsbi
```

## TypeScript Usage

Full TypeScript support with strict mode:

```typescript
import { Connector, Packet, ConnectorConfig } from '@playhouse/connector';

// Fully typed configuration
const config: ConnectorConfig = {
    heartbeatIntervalMs: 5000,
    requestTimeoutMs: 10000,
    enableReconnect: true
};

// Type-safe callbacks
connector.onReceive = (packet: Packet): void => {
    const msgId: string = packet.msgId;
    const stageId: bigint = packet.stageId;
    const errorCode: number = packet.errorCode;
    // ...
};

// Generic proto parsing
interface EchoReply {
    content: string;
    sequence: number;
}

const reply = response.parse<EchoReply>(EchoReply);
```

## LZ4 Compression

When `originalSize > 0` in response packets, payload is LZ4 compressed:

```typescript
import { decompress } from 'lz4js';

function decompressPayload(packet: Packet): Uint8Array {
    if (packet.originalSize > 0) {
        return decompress(packet.payload, packet.originalSize);
    }
    return packet.payload;
}
```

**Dependency**: `lz4js@0.2.0`

## TLS/SSL Support

### Node.js (TLS)

TLS support is planned. Current workaround:
- Use stunnel or nginx as TLS termination proxy

### Browser (WSS)

WebSocket Secure is natively supported:

```typescript
// Use wss:// for secure connection
await connector.connect('wss://game.example.com', 443);
```

## Troubleshooting

### Connection Fails

```typescript
// Enable debug logging
connector.onError = (code, message) => {
    console.error(`[PlayHouse] Error ${code}: ${message}`);
};
```

Check network configuration:
- Verify host and port are correct
- Ensure CORS is configured for browser connections
- Check WebSocket gateway is running (browser)

### Request Timeout

```typescript
// Increase timeout
connector.init({
    requestTimeoutMs: 60000  // 60 seconds
});
```

### Callbacks Not Firing (Browser)

Ensure `mainThreadAction()` is called in animation frame:

```typescript
function gameLoop() {
    connector.mainThreadAction();
    requestAnimationFrame(gameLoop);
}
requestAnimationFrame(gameLoop);
```

### WebSocket Connection Issues

```typescript
// Check WebSocket state
if (connector.isConnected) {
    console.log('Connected via WebSocket');
} else {
    console.log('Not connected');
}

// Handle reconnection
connector.onDisconnect = () => {
    setTimeout(() => {
        connector.connect(host, port).catch(console.error);
    }, 5000);
};
```

### Bundle Size Optimization

For browser builds, ensure tree-shaking works:

```typescript
// Good: Named imports (tree-shakeable)
import { Connector, Packet } from '@playhouse/connector';

// Avoid: Namespace imports
import * as PlayHouse from '@playhouse/connector';
```

## References

- [C# Connector](../csharp/) - Reference implementation
- [Protocol Spec](../../docs/architecture/protocol-spec.md)

## License

Apache 2.0 with Commons Clause
