using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using NeutronNetwork.Internal.Components;
using NeutronNetwork.Constants;
using NeutronNetwork.Server.Internal;
using NeutronNetwork.Helpers;
using System.Net;
using System.Threading.Tasks;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
///* Criado por: Ruan Cardoso(Brasil)
///* Os br também são pica.
///* Email: cardoso.ruan050322@gmail.com
///* Licença: GNU AFFERO GENERAL PUBLIC LICENSE
/// </summary>
namespace NeutronNetwork.Server
{
    ///* Esta classe é o núcleo do Neutron, aqui é o lado do servidor, você pode fazer oque quiser.
    ///* Desde que saiba oque está fazendo skksaksaksak, eu não sei, aqui é só sorte e paciência.
    ///* Um salve pra Unity Brasil.
    [RequireComponent(typeof(NeutronMain))]
    [RequireComponent(typeof(NeutronSchedule))]
    [RequireComponent(typeof(NeutronFPS))]
    [RequireComponent(typeof(NeutronStatistics))]
    [DefaultExecutionOrder(ExecutionOrder.NEUTRON_SERVER)]
    public class NeutronServer : ServerPackets
    {
        #region Events
        /// <summary>
        ///* Este evento é acionando quando o servidor é iniciado.
        /// </summary>
        public static NeutronEventNoReturn OnServerStart;
        /// <summary>
        ///* Este evento é acionado quando um jogador é conectado ao servidor.
        /// </summary>
        public static NeutronEventNoReturn<NeutronPlayer> OnPlayerConnected;
        #endregion

        #region Properties
        /// <summary>
        ///* Obtém o status do servidor.
        /// </summary>
        public static bool Initialized { get; set; }
        /// <summary>
        ///* Objeto de jogador que representa o servidor.
        /// </summary>
        public static NeutronPlayer Player = new NeutronPlayer()
        {
            IsServer = true,
            Nickname = "Server",
            ID = 0,
        };
        #endregion

        #region Fields -> Collections
        //* Esta fila irá armazenar os clientes para serem desinfileirados e processados, em um único Thread(Segmento).
        private readonly NeutronBlockingQueue<TcpClient> _acceptedClients = new NeutronBlockingQueue<TcpClient>(Settings.BOUNDED_CAPACITY);
        //* Esta fila irá armazenar os pacotes recebidos dos clientes para serem desinfileirados e processados, em um único Thread(Segmento).
        private readonly NeutronBlockingQueue<NeutronData> _dataForProcessing = new NeutronBlockingQueue<NeutronData>(Settings.BOUNDED_CAPACITY);
        //* Esta fila irá fornecer um pool de ID's e atribuirá um ID único para cada cliente novo.
        //* Quando um cliente for desconectado seu ID será reutilizado sendo adicionado a fila novamente.
        public NeutronSafeQueue<int> _pooledIds = new NeutronSafeQueue<int>();
        #endregion

        #region Threading
        //* Este é um token de cancelamento, ele é passado para todos os Threads, é usado para parar os Threads quando o servidor for desligado.
        private readonly CancellationTokenSource TokenSource = new CancellationTokenSource();
        #endregion

        #region Functions
        private void Initilize()
        {
            //* Esta região irá fornecer os ID para a lista.
            #region Provider
            for (int i = 0; i < NeutronMain.Settings.GlobalSettings.MaxPlayers; i++)
                _pooledIds.Enqueue((Settings.GENERATE_PLAYER_ID + i) + 1);
            #endregion

            //* Marca o servidor como inicializado.
            Initialized = true;

            #region Logger
            LogHelper.Info("The Server is ready, all protocols(TCP, UDP, RUDP) have been initialized.\r\n");
            #endregion

            //* Este thread será dedicado a aceitar e enfileirar os novos clientes.
            #region Threads
            Thread acptTh = new Thread((t) => OnAcceptedClient())
            {
                Priority = System.Threading.ThreadPriority.Normal,
                IsBackground = true,
                Name = "Neutron acptTh"
            };
            acptTh.Start();

            //* Este thread será dedicado a desinfileirar os pacotes e processa-los.
            Thread packetProcessingStackTh = new Thread((e) => PacketProcessingStack())
            {
                Priority = System.Threading.ThreadPriority.Highest,
                IsBackground = true,
                Name = "Neutron packetProcessingStackTh"
            };
            packetProcessingStackTh.Start();

            //* Este thread será dedicado a desinfileirar os novos clientes e processa-los.
            Thread clientsProcessingStackTh = new Thread((e) => ClientsProcessingStack())
            {
                Priority = System.Threading.ThreadPriority.Normal,
                IsBackground = true,
                Name = "Neutron ClientsProcessingStackTh"
            };
            clientsProcessingStackTh.Start();
            #endregion

            #region Events
            OnServerStart?.Invoke();
            #endregion
        }

        //* Aceita os clientes e os adiciona a fila.
        private async void OnAcceptedClient()
        {
            CancellationToken token = TokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await TcpListener.AcceptTcpClientAsync(); //* Aceita a nova conexão,
                    {
                        _acceptedClients.Add(client, token); //* Nova conexão é enfileirada para processamento.
                    }
                }
                catch (ObjectDisposedException) { continue; }
                catch (OperationCanceledException) { continue; }
                catch (Exception ex)
                {
                    LogHelper.StackTrace(ex);
                    continue;
                }
            }
        }

        //* Inicia o processamento dos clientes.
        private void ClientsProcessingStack()
        {
            CancellationToken token = TokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = _acceptedClients.Take(token); //* Desinfileira os clientes e bloqueia o thread se não houver mais dados.
                    if (PlayerHelper.GetAvailableID(out int ID))
                    {
                        if (SocketHelper.AllowConnection(client)) //* Verifica se este cliente antigiu o limite de conexões ativas.
                        {
                            client.NoDelay = OthersHelper.GetSettings().GlobalSettings.NoDelay;
                            // TODO acceptedClient.ReceiveTimeout = int.MaxValue;
                            // TODO acceptedClient.SendTimeout = int.MaxValue;
                            var player = new NeutronPlayer(ID, client, new CancellationTokenSource()); //* Cria uma instância do cliente.
                            if (SocketHelper.AddPlayer(player))
                            {
                                Interlocked.Increment(ref PlayerCount); //* Incrementa a quantidade de jogadores do servidor.
                                //* Esta região cria um View, um View é usado para você criar uma comunicação personalizada com o cliente dono(owner).
                                //* Exemplo, dentro do View você pode implementar uma função que envia um evento ou mensagem a cada X Segundos.
                                #region View
                                NeutronSchedule.ScheduleTask(() =>
                                {
                                    GameObject viewGameObject = new GameObject($"View[{player.ID}]");
                                    View viewInstance = null;
                                    if (View != null)
                                        viewInstance = (View)viewGameObject.AddComponent(View.GetType());
                                    else
                                    {
                                        viewInstance = viewGameObject.AddComponent<View>();
                                        if (View == null)
                                            View = gameObject.AddComponent<View>();
                                    }
                                    viewInstance.Player = player;
                                    SceneHelper.MoveToContainer(viewGameObject, "[Container] -> Server");
                                });
                                #endregion

                                LogHelper.Info($"Incoming client, IP: [{((IPEndPoint)player.TcpClient.Client.RemoteEndPoint).Address}] | TCP: [{((IPEndPoint)player.TcpClient.Client.RemoteEndPoint).Port}] | UDP: [{((IPEndPoint)player.UdpClient.Client.LocalEndPoint).Port}] -:[{PlayerCount}]");

                                //* Usa o pool de threads para receber os dados do cliente de forma assíncrona.
                                //* O Pool de threads só deverá ser usado com metódos que não bloqueiam, para evitar gargalos no recebimento de dados.
                                ThreadPool.QueueUserWorkItem((e) =>
                                {
                                    OnReceivingData(player, Protocol.Tcp);
                                    OnReceivingData(player, Protocol.Udp);
                                });
                                OnPlayerConnected?.Invoke(player);
                            }
                            else
                            {
                                if (!LogHelper.Error("Failed to add Player!"))
                                    client.Close();
                                continue;
                            }
                        }
                        else
                        {
                            if (!LogHelper.Error("Client not allowed!"))
                                client.Close();
                            continue;
                        }
                    }
                    else
                    {
                        if (!LogHelper.Error("Max Players Reached"))
                            client.Close();
                        continue;
                    }
                }
                catch (ObjectDisposedException) { continue; }
                catch (OperationCanceledException) { continue; }
                catch (Exception ex)
                {
                    LogHelper.StackTrace(ex);
                    continue;
                }
            }
        }

        //* Inicia o processamento dos pacotes.
        private void PacketProcessingStack()
        {
#if NEUTRON_DEBUG || UNITY_EDITOR
            PacketProcessingStack_ManagedThreadId = ThreadHelper.GetThreadID();
#endif
            CancellationToken token = TokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    for (int i = 0; i < NeutronMain.Settings.ServerSettings.PacketsProcessedPerTick; i++)
                    {
                        NeutronData data = _dataForProcessing.Take(token); //* Desinfileira os dados e bloqueia o thread se não houver mais dados.
                        RunPacket(data.Player, data.Buffer); //* Processa/executa o pacote.
                    }
                }
                catch (ObjectDisposedException) { continue; }
                catch (OperationCanceledException) { continue; }
                catch (Exception ex)
                {
                    LogHelper.StackTrace(ex);
                    continue;
                }
            }
        }

        //* Aqui os dados são enviados aos seus clientes.
        public async void OnSendingData(NeutronPlayer player, NeutronData data)
        {
            try
            {
                #region Lag Simulation
                if (NeutronMain.Settings.LagSimulationSettings.Outbound)
                    await Task.Delay(NeutronMain.Settings.LagSimulationSettings.InOutDelay);
                #endregion

                using (NeutronWriter headerWriter = Neutron.PooledNetworkWriters.Pull())
                {
                    byte[] packetBuffer = data.Buffer.Compress();
                    //* Cabeçalho da mensagem/dados.
                    #region Header
                    headerWriter.WriteExactly(packetBuffer); //* Pre-fixa o tamanho da mensagem no cabeçalho, um inteiro(4 bytes), e a mensagem.
                    headerWriter.Write(data.Player.ID); //* Pre-fixa o id do jogador no cabeçalho, um inteiro(4 bytes).
                    #endregion
                    byte[] headerBuffer = headerWriter.ToArray();
                    switch (data.Protocol)
                    {
                        case Protocol.Tcp:
                            {
                                NetworkStream networkStream = player.TcpClient.GetStream();
                                await networkStream.WriteAsync(headerBuffer, 0, headerBuffer.Length, player.TokenSource.Token);
#if UNITY_EDITOR
                                //* Adiciona no profiler a quantidade de dados de saída(Outgoing).
                                NeutronStatistics.m_ServerTCP.AddOutgoing(packetBuffer.Length, data.Packet);
#endif
                            }
                            break;
                        case Protocol.Udp:
                            {
                                if (player.RemoteEndPoint != null) //* Verifica se o IP de destino não é nulo, se o ip de destino for nulo, o servidor não enviará os dados.
                                {
                                    await player.UdpClient.SendAsync(headerBuffer, headerBuffer.Length, player.RemoteEndPoint);
#if UNITY_EDITOR
                                    //* Adiciona no profiler a quantidade de dados de saída(Outgoing).
                                    NeutronStatistics.m_ServerUDP.AddOutgoing(packetBuffer.Length, data.Packet);
#endif
                                }
                            }
                            break;
                    }
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        public void OnSimulatingReceivingData(NeutronData data)
        {
            _dataForProcessing.Add(data);
        }

        //* Recebe/Ler os dados dos clientes.
        private async void OnReceivingData(NeutronPlayer player, Protocol protocol)
        {
            CancellationToken token = player.TokenSource.Token;
            try
            {
                Packet packet;
                byte[] headerBuffer = new byte[sizeof(int)]; //* aqui será armazenado o pre-fixo(tamanho/length) do pacote, que é o tamanho da mensagem transmitida.
                NetworkStream networkStream = player.TcpClient.GetStream();

                while (!TokenSource.Token.IsCancellationRequested && !token.IsCancellationRequested) // Interrompe o loop em caso de cancelamento do Token, o cancelamento ocorre em desconexões ou exceções.
                {
                    switch (protocol)
                    {
                        case Protocol.Tcp:
                            {
                                if (await SocketHelper.ReadAsyncBytes(networkStream, headerBuffer, 0, sizeof(int), token)) //* ler o pre-fixo, um inteiro, 4 bytes(sizeof(int)) e armazena no buffer.
                                {
                                    int size = BitConverter.ToInt32(headerBuffer, 0); //* converte o buffer em inteiro.
                                    if (size > OthersHelper.GetSettings().MAX_MSG_TCP || size <= 0) //* Verifica se o tamanho da mensagem é válido.
                                    {
                                        if (!LogHelper.Error("Invalid tcp message size!"))
                                            DisconnectHandler(player); //* Desconecta o cliente caso a leitura falhe, a leitura falhará em caso de desconexão...etc.
                                    }
                                    else
                                    {
                                        byte[] packetBuffer = new byte[size]; //* cria um buffer com o tamanho da mensagem/pre-fixo.
                                        if (await SocketHelper.ReadAsyncBytes(networkStream, packetBuffer, 0, size, token)) //* ler a mensagem e armazena no buffer de mensagem.
                                        {
                                            packetBuffer = packetBuffer.Decompress();
#if UNITY_EDITOR
                                            packet = OthersHelper.ReadPacket(packetBuffer);
#endif
                                            _dataForProcessing.Add(new NeutronData(packetBuffer, player, Protocol.Tcp, packet), token); //* Adiciona os dados na fila para processamento.
                                            {
#if UNITY_EDITOR
                                                //* Adiciona no profiler a quantidade de dados de entrada(Incoming).
                                                NeutronStatistics.m_ServerTCP.AddIncoming(size, packet);
#endif
                                            }
                                        }
                                        else
                                            DisconnectHandler(player); //* Desconecta o cliente caso a leitura falhe, a leitura falhará em caso de desconexão...etc.
                                    }
                                }
                                else
                                    DisconnectHandler(player); //* Desconecta o cliente caso a leitura falhe, a leitura falhará em caso de desconexão...etc.
                            }
                            break;
                        case Protocol.Udp:
                            {
                                var datagram = await player.UdpClient.ReceiveAsync(); //* Recebe os dados enviados pelo cliente.
                                using (NeutronReader reader = Neutron.PooledNetworkReaders.Pull())
                                {
                                    // Monta o cabeçalho dos dados e ler deus dados.
                                    reader.SetBuffer(datagram.Buffer);
                                    ////////////////////////////////////////////////////////////////////////////////////////////
                                    byte[] packetBuffer = reader.ReadExactly(out int size); //* ler o pacote.
                                    if (size > OthersHelper.GetSettings().MAX_MSG_UDP || size <= 0)
                                    {
                                        if (!LogHelper.Error("Invalid udp message size!"))
                                            DisconnectHandler(player); //* Desconecta o cliente caso a leitura falhe, a leitura falhará em caso de desconexão...etc.
                                    }
                                    else
                                    {
                                        //* descomprime a porra do pacote.
                                        packetBuffer = packetBuffer.Decompress();
                                        //* Esta região funciona como um "Syn/Ack", o cliente envia algum pacote vazio após a conexão, após o servidor receber este pacote, atribui o ip de destino, que é para onde os dados serão enviados.
                                        //! Se o ip de destino for nulo, o servidor não enviará os dados, porque não tem destino, não houve "Syn/Ack".
                                        //! A tentativa de envio sem o "Syn/Ack" causará a exceção de "An existing connection was forcibly closed by the remote host"
                                        #region Syn/Ack
                                        if (player.RemoteEndPoint == null) //* verifica se o ip de destino é nulo, se for, ele é atribuído com o ip de destino.
                                            player.RemoteEndPoint = datagram.RemoteEndPoint; //* ip de destino do cliente, para onde o servidor irá enviar os dados.
                                        #endregion
#if UNITY_EDITOR
                                        packet = OthersHelper.ReadPacket(packetBuffer);
#endif
                                        _dataForProcessing.Add(new NeutronData(packetBuffer, player, Protocol.Udp, packet), token); //* Adiciona os dados na fila para processamento.
                                        {
#if UNITY_EDITOR
                                            //* Adiciona no profiler a quantidade de dados de entrada(Incoming).
                                            NeutronStatistics.m_ServerUDP.AddIncoming(packetBuffer.Length, packet);
#endif
                                        }
                                    }
                                }
                            }
                            break;
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogHelper.StackTrace(ex);
                if (!token.IsCancellationRequested)
                    DisconnectHandler(player);
            }
        }
        #endregion

        #region Packets
        //* Aqui os pacotes serão processados, seus parâmetros serão lidos, e executado sua respectiva função.
        void RunPacket(NeutronPlayer player, byte[] data)
        {
#if UNITY_SERVER || UNITY_EDITOR
            using (NeutronReader reader = Neutron.PooledNetworkReaders.Pull())
            {
                reader.SetBuffer(data);
                switch (reader.ReadPacket<Packet>()) //* Ler o pacote recebido
                {
                    case Packet.Handshake:
                        {
                            #region Logic
                            HandshakeHandler(player, reader.ReadDouble());
                            #endregion
                        }
                        break;
                    case Packet.Nickname:
                        {
                            #region Reader
                            string nickname = reader.ReadString();
                            #endregion

                            #region Logic
                            SetNicknameHandler(player, nickname);
                            #endregion
                        }
                        break;
                    case Packet.Chat:
                        {
                            #region Defaults
                            TunnelingTo tunnelingTo = default(TunnelingTo);
                            int viewId = default(int);
                            #endregion

                            #region Reader
                            ChatPacket chatPacket = reader.ReadPacket<ChatPacket>();
                            switch (chatPacket)
                            {
                                case ChatPacket.Global:
                                    tunnelingTo = reader.ReadPacket<TunnelingTo>();
                                    break;
                                case ChatPacket.Private:
                                    viewId = reader.ReadInt32();
                                    break;
                            }
                            string message = reader.ReadString();
                            #endregion

                            #region Logic
                            ChatHandler(player, chatPacket, tunnelingTo, viewId, message);
                            #endregion
                        }
                        break;
                    case Packet.iRPC:
                        {
                            #region Reader
                            TunnelingTo tunnelingTo = reader.ReadPacket<TunnelingTo>();
                            TargetTo targetTo = reader.ReadPacket<TargetTo>();
                            Cache cache = reader.ReadPacket<Cache>();
                            Protocol protocol = reader.ReadPacket<Protocol>();
                            int viewId = reader.ReadInt32();
                            int id = reader.ReadInt32();
                            byte[] buffer = reader.ReadExactly();
                            #endregion

                            #region Logic
                            iRPCHandler(player, tunnelingTo, targetTo, cache, viewId, id, buffer, protocol);
                            #endregion
                        }
                        break;
                    case Packet.gRPC:
                        {
                            #region Reader
                            int viewId = reader.ReadInt32();
                            int id = reader.ReadInt32();
                            byte[] buffer = reader.ReadExactly();
                            #endregion

                            #region Logic
                            gRPCHandler(player, viewId, id, buffer);
                            #endregion
                        }
                        break;
                    case Packet.GetChannels:
                        {
                            #region Logic
                            GetChannelsHandler(player);
                            #endregion
                        }
                        break;
                    case Packet.JoinChannel:
                        {
                            #region Reader
                            int channelId = reader.ReadInt32();
                            #endregion

                            #region Logic
                            JoinChannelHandler(player, channelId);
                            #endregion
                        }
                        break;
                    case Packet.GetChached:
                        {
                            #region Reader
                            CachedPacket cachedPacket = reader.ReadPacket<CachedPacket>();
                            int packetId = reader.ReadInt32();
                            bool includeMe = reader.ReadBoolean();
                            #endregion

                            #region Logic
                            GetCacheHandler(player, cachedPacket, packetId, includeMe);
                            #endregion
                        }
                        break;
                    case Packet.CreateRoom:
                        {
                            #region Reader
                            string password = reader.ReadString();
                            NeutronRoom room = reader.ReadExactly<NeutronRoom>();
                            #endregion

                            #region Logic
                            CreateRoomHandler(player, room, password);
                            #endregion
                        }
                        break;
                    case Packet.GetRooms:
                        {
                            #region Logic
                            GetRoomsHandler(player);
                            #endregion
                        }
                        break;
                    case Packet.JoinRoom:
                        {
                            #region Reader
                            int roomId = reader.ReadInt32();
                            #endregion

                            #region Logic
                            JoinRoomHandler(player, roomId);
                            #endregion
                        }
                        break;
                    case Packet.Leave:
                        {
                            #region Reader
                            MatchmakingPacket packet = reader.ReadPacket<MatchmakingPacket>();
                            #endregion

                            #region Logic
                            if (packet == MatchmakingPacket.Room)
                                LeaveRoomHandler(player);
                            else if (packet == MatchmakingPacket.Channel)
                                LeaveChannelHandler(player);
                            #endregion
                        }
                        break;
                    case Packet.DestroyPlayer:
                        {
                            #region Logic
                            DestroyPlayerHandler(player);
                            #endregion
                        }
                        break;
                    case Packet.SetPlayerProperties:
                        {
                            #region Reader
                            string properties = reader.ReadString();
                            #endregion

                            #region Logic
                            SetPlayerPropertiesHandler(player, properties);
                            #endregion
                        }
                        break;
                    case Packet.SetRoomProperties:
                        {
                            #region Reader
                            string properties = reader.ReadString();
                            #endregion

                            #region Logic
                            SetRoomPropertiesHandler(player, properties);
                            #endregion
                        }
                        break;
                    case Packet.Ping:
                        {
                            #region Reader
                            double time = reader.ReadDouble();
                            #endregion

                            #region Logic
                            PingHandler(player, time);
                            #endregion
                        }
                        break;
                    case Packet.CustomPacket:
                        {
                            #region Defaults
                            bool isMine = false;
                            TargetTo targetTo = default(TargetTo);
                            TunnelingTo tunnelingTo = default(TunnelingTo);
                            #endregion

                            #region Reader
                            int viewId = reader.ReadInt32();
                            CustomPacket packet = reader.ReadPacket<CustomPacket>();
                            if ((isMine = PlayerHelper.IsMine(player, viewId)))
                            {
                                targetTo = reader.ReadPacket<TargetTo>();
                                tunnelingTo = reader.ReadPacket<TunnelingTo>();
                            }
                            Protocol protocol = reader.ReadPacket<Protocol>();
                            byte[] buffer = reader.ReadExactly();
                            #endregion

                            #region Logic
                            CustomPacketHandler(player, isMine, viewId, buffer, packet, targetTo, tunnelingTo, protocol);
                            #endregion
                        }
                        break;
                    case Packet.OnAutoSync:
                        {
                            #region Reader
                            Protocol protocol = reader.ReadPacket<Protocol>();
                            RegisterType registerType = reader.ReadPacket<RegisterType>();
                            TargetTo targetTo = reader.ReadPacket<TargetTo>();
                            TunnelingTo tunnelingTo = reader.ReadPacket<TunnelingTo>();
                            int viewId = reader.ReadInt32();
                            int instanceId = reader.ReadInt32();
                            byte[] buffer = reader.ReadExactly();
                            #endregion

                            #region Logic
                            OnAutoSyncHandler(player, viewId, instanceId, buffer, registerType, targetTo, tunnelingTo, protocol);
                            #endregion
                        }
                        break;
                }
            }
#endif
        }
        #endregion

        #region MonoBehaviour
        private void Start()
        {
#if UNITY_SERVER
            Console.Clear();
#endif

#if UNITY_EDITOR
            var targetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            var acl = PlayerSettings.GetApiCompatibilityLevel(targetGroup);
            if (acl != ApiCompatibilityLevel.NET_Standard_2_0)
            {
                if (IsReady)
                    Initilize();
            }
            else
                LogHelper.Error(".NET Standard is not supported, change to .NET 4.x.");
#else
            if (IsReady)
                Initilize();
#endif
        }

        private async void OnApplicationQuit()
        {
            using (TokenSource)
            {
                if (Initialized)
                {
                    Initialized = false;
                    TokenSource.Cancel();
                    await Task.Delay(50);
                    foreach (var player in PlayersById.Values)
                    {
                        player.TokenSource.Cancel();
                        await Task.Delay(20);
                        player.Dispose();
                    }
                    _acceptedClients.Dispose();
                    _dataForProcessing.Dispose();
                    TcpListener.Stop();
                }
            }
        }
        #endregion
    }
}