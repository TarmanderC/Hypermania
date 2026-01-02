use axum::{
    Router,
    extract::{
        Path, Query, State,
        ws::{Message, WebSocket, WebSocketUpgrade},
    },
    response::Response,
    routing::get,
};
use clap::Parser;
use futures_util::{sink::SinkExt, stream::StreamExt};
use serde::Deserialize;
use std::net::SocketAddr;
use std::{collections::HashMap, sync::Arc};
use tokio::sync::{RwLock, mpsc};
use tower_http::trace::TraceLayer;
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

use crate::{error::ApiError, relay::udp_server, utils::UdpClientState};

mod error;
mod relay;
mod utils;

type RoomId = u64;
type ClientId = u128;
type ClientString = String;
type Handle = u32;

#[derive(Clone)]
struct AppState {
    room_state: Arc<RwLock<AppRoomState>>,
    udp_state: Arc<RwLock<AppUdpState>>,
}

impl AppState {
    async fn cleanup_udp(&self, client_id: ClientId) {
        let mut udp_state = self.udp_state.write().await;
        if let Some(ep) = udp_state.udp_addrs.remove(&client_id) {
            udp_state.addr_to_client.remove(&ep.udp_addr);
        }
    }
}

struct AppRoomState {
    next_room_id: RoomId,
    rooms: HashMap<RoomId, RoomState>,
    clients: HashMap<ClientId, ClientState>,
    ws_peers: HashMap<ClientId, mpsc::UnboundedSender<Message>>,
}

impl AppRoomState {
    pub fn get_peer(&self, client_id: ClientId) -> Option<ClientId> {
        let client = self.clients.get(&client_id)?;
        let room = self.rooms.get(&client.room)?;
        let guest_id = room.client?;
        let other_id = if room.host == client_id {
            guest_id
        } else {
            room.host
        };
        Some(other_id)
    }

    pub fn send_to(&self, dst: ClientId, msg: Message) {
        if let Some(tx) = self.ws_peers.get(&dst) {
            let _ = tx.send(msg);
        }
    }
}

struct AppUdpState {
    udp_addrs: HashMap<ClientId, UdpClientState>,
    addr_to_client: HashMap<SocketAddr, ClientId>,
}

#[derive(Default)]
struct RoomState {
    host: ClientId,
    client: Option<ClientId>,
}

struct ClientState {
    room: RoomId,
}

impl ClientState {
    fn new(room_id: RoomId) -> Self {
        Self { room: room_id }
    }
}

#[derive(Parser, Debug, Clone)]
#[command(name = "rendezvous-server")]
struct Args {
    #[arg(long, default_value_t = 9000)]
    http_port: u16,
    #[arg(long, default_value_t = 9001)]
    relay_port: u16,
}

fn bind_addr(port: u16) -> SocketAddr {
    SocketAddr::from(([0, 0, 0, 0], port))
}
#[repr(u8)]
enum InWsEventType {
    StartGame = 1,
}

enum InWsEvent {
    StartGame,
}

impl InWsEvent {
    fn decode(msg: &Message) -> Option<Self> {
        let Message::Binary(b) = msg else {
            return None;
        };
        if b.is_empty() {
            return None;
        }
        match b[0] {
            x if x == InWsEventType::StartGame as u8 => Some(InWsEvent::StartGame),
            _ => None,
        }
    }
}

#[repr(u8)]
enum OutWsEventType {
    JoinedRoom = 1,
    YouAre = 2,
    PeerJoined = 3,
    PeerLeft = 4,
    StartedGame = 5,
}

enum OutWsEvent {
    JoinedRoom(RoomId),
    YouAre(Handle),
    PeerJoined(Handle),
    PeerLeft(Handle),
    StartedGame,
}

impl OutWsEvent {
    fn encode(&self) -> Vec<u8> {
        match *self {
            OutWsEvent::JoinedRoom(room_id) => {
                let mut out = Vec::with_capacity(1 + 8);
                out.push(OutWsEventType::JoinedRoom as u8);
                out.extend_from_slice(&room_id.to_be_bytes());
                out
            }
            OutWsEvent::YouAre(handle) => {
                let mut out = Vec::with_capacity(1 + 4);
                out.push(OutWsEventType::YouAre as u8);
                out.extend_from_slice(&handle.to_be_bytes());
                out
            }
            OutWsEvent::PeerJoined(handle) => {
                let mut out = Vec::with_capacity(1 + 4);
                out.push(OutWsEventType::PeerJoined as u8);
                out.extend_from_slice(&handle.to_be_bytes());
                out
            }
            OutWsEvent::PeerLeft(handle) => {
                let mut out = Vec::with_capacity(1 + 4);
                out.push(OutWsEventType::PeerLeft as u8);
                out.extend_from_slice(&handle.to_be_bytes());
                out
            }
            OutWsEvent::StartedGame => {
                let mut out = Vec::with_capacity(1);
                out.push(OutWsEventType::StartedGame as u8);
                out
            }
        }
    }

    fn into_message(self) -> Message {
        Message::Binary(self.encode().into())
    }
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let args = Args::parse();

    let room_state = Arc::new(RwLock::new(AppRoomState {
        next_room_id: 0,
        rooms: HashMap::new(),
        clients: HashMap::new(),
        ws_peers: HashMap::new(),
    }));
    let udp_state = Arc::new(RwLock::new(AppUdpState {
        udp_addrs: HashMap::new(),
        addr_to_client: HashMap::new(),
    }));
    let state = AppState {
        room_state,
        udp_state,
    };

    tracing_subscriber::registry()
        .with(
            tracing_subscriber::EnvFilter::try_from_default_env().unwrap_or_else(|_| {
                format!(
                    "{}=debug,tower_http=debug,axum::rejection=trace",
                    env!("CARGO_CRATE_NAME")
                )
                .into()
            }),
        )
        .with(tracing_subscriber::fmt::layer())
        .init();

    let relay_addr = bind_addr(args.relay_port);
    let http_addr = bind_addr(args.http_port);

    tracing::info!(
        "Starting server with tcp port {} relay port {}",
        args.http_port,
        args.relay_port
    );

    tokio::spawn(udp_server(relay_addr, state.clone()));

    let app = Router::new()
        .layer(TraceLayer::new_for_http())
        .route("/create_room", get(ws_create_room))
        .route("/join_room/{room_id}", get(ws_join_room))
        .with_state(state);

    let listener = tokio::net::TcpListener::bind(http_addr).await?;
    axum::serve(listener, app).await?;
    Ok(())
}

#[derive(Deserialize)]
struct WsClientParams {
    client_id: ClientString,
}

async fn ws_create_room(
    ws: WebSocketUpgrade,
    State(st): State<AppState>,
    Query(q): Query<WsClientParams>,
) -> Result<Response, ApiError> {
    let Ok(client_id) = q.client_id.parse::<u128>() else {
        return Err(ApiError::BadRequest("could not parse client id"));
    };

    let mut state = st.room_state.write().await;
    let room_id = state.next_room_id;
    state.next_room_id = state
        .next_room_id
        .checked_add(1)
        .ok_or(ApiError::Conflict("room id overflow"))?;

    state.rooms.insert(
        room_id,
        RoomState {
            host: client_id,
            client: None,
        },
    );
    state
        .clients
        .entry(client_id)
        .and_modify(|e| e.room = room_id)
        .or_insert(ClientState::new(room_id));
    drop(state);

    Ok(ws.on_upgrade(move |socket| client_ws_loop(socket, st, client_id, room_id, 0)))
}

async fn ws_join_room(
    ws: WebSocketUpgrade,
    State(st): State<AppState>,
    Path(room_id): Path<u64>,
    Query(q): Query<WsClientParams>,
) -> Result<Response, ApiError> {
    let Ok(client_id) = q.client_id.parse::<u128>() else {
        return Err(ApiError::BadRequest("could not parse client id"));
    };

    let mut state = st.room_state.write().await;
    if state
        .clients
        .get(&client_id)
        .is_some_and(|e| e.room != room_id)
    {
        return Err(ApiError::Conflict("client is already in another room"));
    }
    let Some(room) = state.rooms.get_mut(&room_id) else {
        return Err(ApiError::NotFound("room not found"));
    };
    if room.client.is_some() {
        return Err(ApiError::Conflict("room is full"));
    }
    room.client = Some(client_id);
    state
        .clients
        .entry(client_id)
        .and_modify(|e| e.room = room_id)
        .or_insert(ClientState::new(room_id));
    drop(state);

    Ok(ws.on_upgrade(move |socket| client_ws_loop(socket, st, client_id, room_id, 1)))
}

async fn client_ws_loop(
    socket: WebSocket,
    st: AppState,
    client_id: ClientId,
    room_id: RoomId,
    handle: u8,
) {
    let (mut ws_tx, mut ws_rx) = socket.split();
    let (tx, mut rx) = mpsc::unbounded_channel::<Message>();

    {
        let mut rs = st.room_state.write().await;
        rs.ws_peers.insert(client_id, tx);
    }

    let writer = tokio::spawn(async move {
        while let Some(msg) = rx.recv().await {
            if ws_tx.send(msg).await.is_err() {
                break;
            }
        }
    });

    let rs = st.room_state.read().await;
    rs.send_to(client_id, OutWsEvent::JoinedRoom(room_id).into_message());
    rs.send_to(client_id, OutWsEvent::YouAre(handle as u32).into_message());

    if handle != 0 {
        // if we are not host, notify ourself that there is a host and the host that we have joined
        rs.send_to(client_id, OutWsEvent::PeerJoined(0).into_message());
        rs.send_to(
            rs.rooms
                .get(&room_id)
                .expect("to join a room it must exist")
                .host,
            OutWsEvent::PeerJoined(1).into_message(),
        );
    }

    drop(rs);

    while let Some(item) = ws_rx.next().await {
        match item {
            Ok(Message::Close(_)) => break,
            Ok(msg) => {
                if let Some(evt) = InWsEvent::decode(&msg) {
                    match evt {
                        InWsEvent::StartGame => {
                            // broadcast StartedGame to everyone in the room
                            let rs = st.room_state.read().await;
                            if let Some(room) = rs.rooms.get(&room_id) {
                                if let Some(guest) = room.client {
                                    rs.send_to(guest, OutWsEvent::StartedGame.into_message());
                                    rs.send_to(room.host, OutWsEvent::StartedGame.into_message());
                                }
                            }
                        }
                    }
                }
            }
            Err(_) => break,
        }
    }

    leave_room_on_disconnect(&st, client_id).await;
    writer.abort();
}

async fn leave_room_on_disconnect(st: &AppState, client_id: ClientId) {
    let mut state = st.room_state.write().await;

    state.ws_peers.remove(&client_id);

    let Some(client_state) = state.clients.get(&client_id) else {
        drop(state);
        st.cleanup_udp(client_id).await;
        return;
    };
    let cur_room = client_state.room;

    let mut notify: Vec<(ClientId, Message)> = Vec::new();

    let Some(room) = state.rooms.get_mut(&cur_room) else {
        state.clients.remove(&client_id);
        drop(state);
        st.cleanup_udp(client_id).await;
        return;
    };

    if room.client.is_some_and(|id| id == client_id) {
        // guest left -> notify host
        let host = room.host;
        notify.push((host, OutWsEvent::PeerLeft(1).into_message()));
        room.client = None;
    } else {
        // host left
        if client_id != room.host {
            state.clients.remove(&client_id);
            drop(state);
            st.cleanup_udp(client_id).await;
            return;
        }

        match room.client {
            Some(peer) => {
                // transfer host -> peer, notify new host
                room.host = peer;
                room.client = None;
                notify.push((peer, OutWsEvent::PeerLeft(0).into_message()));
                notify.push((peer, OutWsEvent::YouAre(0).into_message()));
            }
            None => {
                state.rooms.remove(&cur_room);
            }
        }
    }

    for (dst, msg) in notify {
        state.send_to(dst, msg);
    }

    state.clients.remove(&client_id);
    drop(state);
    st.cleanup_udp(client_id).await;
}
