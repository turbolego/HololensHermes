# HoloLens Hermes agent interface: remaining work

## Current state

The repository is a HoloLens 1 UWP/Direct3D prototype. It has platform-neutral
venue selection, floor-plan calibration, route validation, and accessible
guidance logic, plus HoloLens rendering and speech-synthesis components.
Those pieces are not yet connected into a working Hermes agent interface:

- The render loop does not instantiate or call `HermesApiService` or
  `TelegramService`; no route is loaded by the running app.
- There is no in-app conversation UI, holographic response-text renderer,
  Telegram update receiver, photo capture/upload flow, or voice-record/upload
  flow.
- The Telegram service currently sends text only, has no public chat-ID setup
  path, and stores a bot token on the device. A Telegram bot cannot use its own
  bot identity to initiate a normal user conversation with another bot.
- The existing `dotnet test` project runs cross-platform navigation logic, not
  the UWP app, network services, camera/microphone, or HoloLens rendering.

The deterministic journey tests in `tests/HololensHermes.Tests` cover the
currently testable path from coarse venue selection through calibration to
world-target guidance. They are not substitutes for the media, Telegram, or
device-level tests below.

## Recommended architecture

Treat Telegram as the communication channel, but keep bot credentials and
Telegram polling on a trusted Hermes-side relay. The HoloLens app should
authenticate to that relay; it should not contain a production bot token.
Define the relay's request/response contract before implementing the client.
Each conversation turn should carry a correlation ID, message type, payload
metadata, and explicit success/error status. Photo and voice bytes should use
bounded uploads rather than being embedded in JSON.

On device, keep capture, networking, conversation state, presentation, and
spatial navigation behind separate adapters. A completed agent response should
be rendered as readable holographic text and spoken using the existing speech
synthesis capability. A navigation intent may additionally produce a validated
route; ordinary assistant text must not be interpreted as a spatial target.

## Work remaining

### 1. Agree on the Hermes/Telegram contract

- Identify the Hermes-side agent endpoint, Telegram relay ownership, user/device
  authentication, conversation/session model, and supported response formats.
- Specify text, image, and audio upload limits, accepted audio codecs, timeouts,
  cancellation, retry/idempotency behavior, and error responses.
- Define response types for plain text, optional synthesized audio, and
  structured navigation intents. Keep Telegram bot tokens off device.
- Decide how the user pairs a HoloLens with an authorized conversation without
  asking for a Telegram account password.

**Acceptance:** a versioned contract and a non-production relay can be exercised
with deterministic fixtures; no bot token is required by the app.

### 2. Build the conversation and transport layer

- Replace the current direct Bot API wrapper with a testable client for the
  authenticated relay, using cancellation, bounded payloads, explicit HTTP
  status handling, and safe diagnostics that never log credentials or media.
- Add text, JPEG photo, and recorded voice request models with validation and
  conversation correlation.
- Handle inbound replies and relay errors without blocking the holographic
  render loop; prevent duplicate submission and stale replies.
- Add unit and HTTP integration tests using a local fake relay for success,
  timeout, malformed response, oversized media, and retry behavior.

**Acceptance:** each request type round-trips through the fake relay and maps
its response or failure to an explicit conversation state.

### 3. Add HoloLens input and capture

- Add a gaze/gesture-accessible conversation panel with status, text input,
  send, photo, voice-record, cancel, and retry actions.
- Capture still images with `MediaCapture`, request runtime permission, validate
  dimensions/size, and dispose camera resources on every exit path.
- Record bounded voice clips, expose recording state and duration, and produce
  a relay-supported encoding. Make cancellation and permission denial visible.
- Keep all camera/microphone work asynchronous and outside frame rendering.

**Acceptance:** on HoloLens 1, text, one photo, and a voice clip can each be
submitted; permission denial, cancellation, and network loss do not hang or
crash the app.

### 4. Present Hermes responses

- Add a readable holographic text panel with wrapping, paging/scrolling,
  contrast, stable placement, and a way to dismiss or repeat the latest reply.
- Connect response text to `SpeechSynthesizer`; handle long text, interruption,
  overlapping turns, and speech synthesis failures without losing the visual
  response.
- Keep conversation text distinct from route instructions. Accept navigation
  only from a validated structured intent and require venue/calibration/tracking
  safeguards before showing movement guidance.

**Acceptance:** the same response is legible in-headset and spoken; visual output
remains available when audio is disabled or unavailable.

### 5. Expand automated and device end-to-end coverage

- Extend the current pure .NET suite with conversation-state tests and local
  relay HTTP integration tests.
- Add a repeatable UWP build/package check on a Windows runner; the current
  cross-platform tests cannot compile or launch the HoloLens app.
- Verify the SharpDX 4.2.0 references in this repository against the companion
  HoloLens app's SharpDX 3.0.2 baseline before reusing its renderer code or
  relying on the .NET Native x86 release pipeline.
- Add a hardware test checklist (or supported device automation) for launch,
  permissions, capture, upload/download, holographic text, speech, spatial
  tracking loss, and recovery.
- Verify privacy declarations, retention, credential handling, image/audio
  limits, and user-visible consent.

**Acceptance:** CI validates core logic and relay contracts; a recorded
HoloLens 1 smoke-test run verifies hardware-dependent behaviors before release.

## Suggested implementation order

1. Agree on and fixture-test the relay contract.
2. Implement conversation state and relay client with local HTTP integration
   tests.
3. Add text-only interaction and holographic text/speech presentation.
4. Add photo capture/upload, then voice recording/upload.
5. Connect structured navigation intents to the existing guarded navigation
   pipeline.
6. Complete HoloLens 1 hardware testing and release/privacy review.
