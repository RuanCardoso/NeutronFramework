﻿using NeutronNetwork.Client;
using NeutronNetwork.Constants;
using NeutronNetwork.Extensions;
using NeutronNetwork.Helpers;
using NeutronNetwork.Internal;
using NeutronNetwork.Internal.Attributes;
using NeutronNetwork.Internal.Components;
using NeutronNetwork.Internal.Packets;
using NeutronNetwork.Packets;
using NeutronNetwork.Server;
using NeutronNetwork.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
///* Criado por: Ruan Cardoso(Brasil)
///* Os br também são pica.
///* Email: cardoso.ruan050322@gmail.com
///* Licença: GNU AFFERO GENERAL PUBLIC LICENSE
/// </summary>
namespace NeutronNetwork
{
    ///* Esta classe é o núcleo do Neutron, aqui é o lado do cliente, você pode fazer oque quiser.
    ///* Um Salve pra Unity Brasil.
    [DefaultExecutionOrder(ExecutionOrder.NEUTRON_CLIENT)]
    public class Neutron : ClientBase
    {
        #region Constructors
        public Neutron() { }
        public Neutron(NeutronPlayer player, bool isConnected, Neutron instance)
        {
            Player = player;
            IsConnected = isConnected;
#if UNITY_SERVER && !UNITY_EDITOR
            Client = instance;
#endif
        }
        #endregion

        #region Static Settings
        private NeutronConstantsSettings Constants => Helper.GetConstants();
        private Settings Settings => Helper.GetSettings();
        #endregion

        #region Collections
        /// <summary>
        ///* Providencia um pool de leitores e escritores, utilize-o para melhor performance.
        /// </summary>
        public static NeutronPool<NeutronStream> PooledNetworkStreams {
            get;
            set;
        }
        //* Providencia um pool de Pacotes, utilize-o para melhor performance.
        public static NeutronPool<NeutronPacket> PooledNetworkPackets {
            get;
            set;
        }
        //* Esta fila irá armazenar os pacotes recebidos dos clientes para serem desinfileirados e processados, em um único Thread(Segmento).
        private readonly NeutronBlockingQueue<NeutronPacket> _dataForProcessing = new NeutronBlockingQueue<NeutronPacket>(/*NeutronConstantsSettings.BOUNDED_CAPACITY*/);
        #endregion

        #region Fields
        private string _host;
        #endregion

        #region Properties -> Static
        /// <summary>
        ///* Retorna a instância do servidor.<br/>
        ///! Só pode ser obtido no lado do servidor ou no Unity Editor, no cliente será nulo.
        /// </summary>
        public static NeutronServer Server {
            get => ServerBase.This;
        }
        /// <summary>
        ///* Retorna a instância principal do Cliente.<br/>
        ///* Retorna a instância principal do Servidor, se estiver sendo executado em uma compilação de servidor.<br/>
        /// </summary>
        public static Neutron Client {
            get;
            private set;
        }
        #endregion

        #region Properties -> Instance
        /// <summary>
        ///* Retorna o objeto que representa seu jogador.
        /// </summary>
        public NeutronPlayer Player {
            get;
            private set;
        }
        /// <summary>
        ///* Retorna o status da sua conexão.
        /// </summary>
        public bool IsConnected {
            get;
            private set;
        }
        /// <summary>
        ///* Obtém o nickname do seu jogador.
        /// </summary>
        public string Nickname {
            get;
            private set;
        }
        #endregion

        #region Properties -> Events
        /// <summary>
        ///* Este evento é acionado quando uma tentativa de conexão retorna seu estado.<br/>
        /// </summary>
        public NeutronEventNoReturn<bool, Neutron> OnNeutronConnected { get; set; }
        /// <summary>
        ///* Este evento é acionado quando uma tentativa de autenticação retorna seu estado.<br/>
        /// </summary>
        public NeutronEventNoReturn<bool, JObject, Neutron> OnNeutronAuthenticated { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum jogador se conecta ao servidor/matchmaking.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronPlayer, bool, Neutron> OnPlayerConnected { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum jogador se desconecta do servidor.<br/>
        /// </summary>
        public NeutronEventNoReturn<string, NeutronPlayer, bool, Neutron> OnPlayerDisconnected { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum jogador envia uma mensagem e esta mensagem é recebida.<br/>
        /// </summary>
        public NeutronEventNoReturn<string, NeutronPlayer, bool, Neutron> OnMessageReceived { get; set; }
        /// <summary>
        ///* Este evento é acionado quando a lista de canais disponíveis é recebida ou atualizada.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronChannel[], Neutron> OnChannelsReceived { get; set; }
        /// <summary>
        ///* Este evento é acionado quando a lista de salas disponíveis é recebida ou atualizada.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronRoom[], Neutron> OnRoomsReceived { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum jogador sai do canal.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronChannel, NeutronPlayer, bool, Neutron> OnPlayerLeftChannel { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum jogador sai da sala.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronRoom, NeutronPlayer, bool, Neutron> OnPlayerLeftRoom { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum jogador cria uma sala.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronRoom, NeutronPlayer, bool, Neutron> OnPlayerCreatedRoom { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum jogador entra na sala.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronRoom, NeutronPlayer, bool, Neutron> OnPlayerJoinedRoom { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum jogador entra no canal.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronChannel, NeutronPlayer, bool, Neutron> OnPlayerJoinedChannel { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum jogador troca seu nickname.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronPlayer, string, bool, Neutron> OnPlayerNicknameChanged { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum jogador atualiza suas propriedades.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronPlayer, string, bool, Neutron> OnPlayerPropertiesChanged { get; set; }
        /// <summary>
        ///* Este evento é acionado quando alguma sala atualiza suas propriedades.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronPlayer, string, bool, Neutron> OnRoomPropertiesChanged { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum jogador envia um pacote personalizado.<br/>
        /// </summary>
        public NeutronEventNoReturn<NeutronStream.IReader, NeutronPlayer, byte, Neutron> OnPlayerCustomPacketReceived { get; set; }
        /// <summary>
        ///* Este evento é acionado quando algum pacote apresenta uma falha.<br/>
        /// </summary>
        public NeutronEventNoReturn<Packet, string, int, Neutron> OnError { get; set; }
        #endregion

        #region Yields
        private WaitForSeconds _yieldUdpKeepAlive;
        private WaitForSeconds _yieldTcpKeepAlive;
        #endregion

        #region Methods -> Instance
        private void OnUpdate() { }
        /// <summary>
        ///* Inicia uma tentativa de estabelecer uma conexão com o servidor.
        /// </summary>
        /// <param name="index">* Index do endereço de ip da lista de configurações</param>
        /// <param name="timeout">* Tempo limite de tentativa de estabelecer uma conexão.</param>
        /// <param name="authentication">* Será feita a tentativa de autenticação antes do cliente estabelecer a conexão.</param>
        /// <returns></returns>
        public async void Connect(int index = 0, int timeout = 3, Authentication authentication = null)
        {
#if UNITY_SERVER && !UNITY_EDITOR
            if (ClientMode == ClientMode.Player)
                return;
#endif
            if (authentication == null)
                authentication = Authentication.Auth;
            if (!IsConnected)
            {
                NeutronModule.OnUpdate += OnUpdate;
                //**********************************
#if UNITY_EDITOR || UNITY_NEUTRON_LAN
                await Task.Delay(5); //* Eu dei prioridade de execução pro servidor, ele inicializa primeiro que geral, mas coloquei esse delay por segurança, vai que o cliente inicia primeiro que o servidor, disponível somente no editor.
#endif
                Initialize();
                TcpClient.NoDelay = Settings.GlobalSettings.NoDelay;
                TcpClient.ReceiveBufferSize = Constants.Tcp.TcpReceiveBufferSize;
                TcpClient.SendBufferSize = Constants.Tcp.TcpSendBufferSize;
                UdpClient.Client.ReceiveBufferSize = Constants.Udp.UdpReceiveBufferSize;
                UdpClient.Client.SendBufferSize = Constants.Udp.UdpSendBufferSize;

                _yieldUdpKeepAlive = new WaitForSeconds(Settings.ClientSettings.UdpKeepAlive); //* evita alocações, performance é prioridade.
                _yieldTcpKeepAlive = new WaitForSeconds(Settings.ClientSettings.TcpKeepAlive); //* evita alocações, performance é prioridade.

                //* Obtém o ip do URL setado nas configurações.
                #region Host Resolver
                int port = Settings.GlobalSettings.Port;
                _host = Settings.GlobalSettings.Addresses[index];
                if (!string.IsNullOrEmpty(_host))
                {
                    if (!IPAddress.TryParse(_host, out IPAddress _))
                    {
                        if (!_host.Equals("localhost", StringComparison.InvariantCultureIgnoreCase))
                        {
                            _host = _host.Replace("http://", string.Empty);
                            _host = _host.Replace("https://", string.Empty);
                            _host = _host.Replace("/", string.Empty);
                            _host = (await SocketHelper.GetHostAddress(_host)).ToString();
                        }
                        else
                            _host = IPAddress.Loopback.ToString();
                    }
                    else
                    { /*Continue*/ }
                }
                else
                    _host = IPAddress.Loopback.ToString();
                #endregion

                bool result = await TcpClient.ConnectAsync(_host, port).RunWithTimeout(new TimeSpan(0, 0, timeout));
                if (!result)
                {
                    OnNeutronConnected?.Invoke(IsConnected, this);
                    if (!LogHelper.Error("An attempt to establish a connection to the server failed."))
                        Dispose();
                }
                else if (result)
                {
                    IsConnected = TcpClient.Connected; //* define o estado da conexão.
                    //* Invoca o evento de conexão, com o estado da conexão.
                    OnNeutronConnected?.Invoke(IsConnected, this);
                    // Obtém o tipo de stream definido nas configurações(networkStream or bufferedStream). bufferedStream aumenta a performance em 10x.
                    Stream networkStream = SocketHelper.GetStream(TcpClient);
                    //* Dedica um thread a receber os dados do servidor, este é o unico thread que vai receber os dados, ele não vai processar os pacotes, apenas receber.
                    //* Terá outro thread, dedicado a processar os pacotes, fiz assim, pra evitar usar sincronização(lock) que é chato para caralho, sfd, por que os dados sempre são recebidos em threads diferentes, o famoso, IOCP Threads do sistema operacional windows ou epoll do Linux.
                    switch (Constants.ReceiveThread)
                    {
                        case ThreadType.Neutron:
                            {
                                ThreadPool.QueueUserWorkItem((e) =>
                                {
                                    OnReceivingData(networkStream, Protocol.Tcp);
                                    OnReceivingData(networkStream, Protocol.Udp);
                                });
                                break;
                            }
                        case ThreadType.Unity:
                            OnReceivingData(networkStream, Protocol.Tcp);
                            OnReceivingData(networkStream, Protocol.Udp);
                            break;
                    }
                    //* Este thread será dedicado a desinfileirar os pacotes e processa-los.
                    Thread packetProcessingStackTh = new Thread((e) => PacketProcessingStack())
                    {
                        Priority = System.Threading.ThreadPriority.Normal,
                        IsBackground = true,
                        Name = "Neutron packetProcessingStackTh"
                    };
                    packetProcessingStackTh.Start();
                    //* Inicia os pacotes de manutenção, para manter a conexão ativa.
                    NeutronSchedule.ScheduleTask(UdpKeepAlive());
                    NeutronSchedule.ScheduleTask(TcpKeepAlive());
                    //* Aguarda a porra da autenticação....
                    HandAuth();
                }
            }
            else
                HandAuth();

            //* Envia um pacote de reconhecimento/autenticação pro servidor e ele responde com suas informações.
            void HandAuth()
            {
                try
                {
                    if (!IsReady)
                    {
                        using (NeutronStream stream = PooledNetworkStreams.Pull())
                        {
                            NeutronStream.IWriter writer = stream.Writer;
                            //**********************************************
                            string appId = Settings.GlobalSettings.AppId;
                            //**********************************************
                            writer.WritePacket((byte)Packet.Handshake);
                            writer.Write(appId.Encrypt());
                            writer.Write(NetworkTime.LocalTime);
                            writer.WriteIntExactly(authentication);
                            //**********************************************
                            Send(stream);
                        }
                    }
                    else
                        LogHelper.Error("It is no longer possible to initialize a connection on this instance.");
                }
                catch
                {
                    LogHelper.Error("It is no longer possible to initialize a connection on this instance.");
                }
            }
        }

        //* Recebe a porra dos dados do socket udp e monta o pacote.
        private void CreateUdpPacket()
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IReader reader = stream.Reader;
                // Monta o cabeçalho dos dados e ler deus dados.
                reader.SetBuffer(StateObject.ReceivedDatagram);
                //*********************************************************************
                byte[] pBuffer = reader.ReadSize(out int size); //* ler o pacote.
                if (size <= Constants.Udp.MaxUdpPacketSize)
                {
                    int playerId = reader.ReadShort(); //* ler o id do jogador que está transmitindo.
                    //* Vamos descompactar o pacote.
                    pBuffer = pBuffer.Decompress();

                    NeutronPlayer player = Players[playerId];
                    //*********************************************************************************************
                    NeutronPacket neutronPacket = Helper.CreatePacket(pBuffer, player, player, Protocol.Udp);
                    //*********************************************************************************************
                    _dataForProcessing.Add(neutronPacket, TokenSource.Token);
#if UNITY_EDITOR
                    //* Adiciona no profiler a quantidade de dados de entrada(Incoming).
                    NeutronStatistics.ClientUDP.AddIncoming(size);
#endif
                }
                else
                    LogHelper.Error($"Packet size exceeds defined limit!! size: {size}");
            }
        }

        //* Ler os dados udp de modo assíncrono.
        private void UdpApmReceive()
        {
            if (TokenSource.Token.IsCancellationRequested)
                return;
            SocketHelper.BeginReadBytes(UdpClient, StateObject, UdpApmEndReceive);
        }

        private void UdpApmEndReceive(IAsyncResult ar)
        {
            EndPoint remoteEp = StateObject.NonAllocEndPoint;
            int bytesRead = SocketHelper.EndReadBytes(UdpClient, ref remoteEp, ar);
            if (bytesRead > 0)
            {
                StateObject.ReceivedDatagram = new byte[bytesRead];
                //***********************************************************************************
                Buffer.BlockCopy(StateObject.Buffer, 0, StateObject.ReceivedDatagram, 0, bytesRead);
                //***********************************************************************************
                CreateUdpPacket();
            }
            UdpApmReceive();
        }

        //* Inicia o processamento dos pacotes.
        //* repetindo código por preguiça.
        private void PacketProcessingStack()
        {
            CancellationToken token = TokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    NeutronPacket packet = _dataForProcessing.Take(token); //* Desinfileira os dados e bloqueia o thread se não houver mais dados.
                    //****************************************************
                    switch (Constants.PacketThread)
                    {
                        case ThreadType.Neutron:
                            RunPacket(packet.Owner, packet.Buffer);
                            break;
                        case ThreadType.Unity:
                            {
                                NeutronSchedule.ScheduleTask(() =>
                                {
                                    RunPacket(packet.Owner, packet.Buffer);
                                });
                            }
                            break;
                    }
                    packet.Recycle(); //* recicla o pacote sakasksaksksak ): sono, 4 da manhã eu aqui ):
                }
                catch (ObjectDisposedException) { continue; }
                catch (OperationCanceledException) { continue; }
                catch (ArgumentNullException) { continue; }
                catch (Exception ex)
                {
                    LogHelper.StackTrace(ex);
                    continue;
                }
            }
        }

        //* Metódo que ler os dados recebidos do servidor.
        private async void OnReceivingData(Stream networkStream, Protocol protocol)
        {
            CancellationToken token = TokenSource.Token;
            try
            {
                bool whileOn = false;
                byte[] hBuffer = new byte[NeutronModule.HeaderSize]; //* aqui será armazenado o pre-fixo(tamanho/length) do pacote, que é o tamanho da mensagem transmitida.
                byte[] pIBuffer = new byte[sizeof(short)]; //* aqui será armazenado o pre-fixo(ID) do jogador, que é o id do jogador que está transmitindo.
                while (!token.IsCancellationRequested && !whileOn) //* Interrompe o loop em caso de cancelamento do Token, o cancelamento ocorre em desconexões ou exceções ou em caso de Asynchronous APM Mode.
                {
                    switch (protocol)
                    {
                        case Protocol.Tcp:
                            {
                                if (await SocketHelper.ReadAsyncBytes(networkStream, hBuffer, 0, NeutronModule.HeaderSize, token)) //* ler o pre-fixo, um inteiro, 4 bytes(sizeof(int)) e armazena no buffer.
                                {
                                    int size = ByteHelper.ReadSize(hBuffer); //* converte o buffer em inteiro.
                                    if (size <= Constants.Tcp.MaxTcpPacketSize)
                                    {
                                        byte[] pBuffer = new byte[size]; //* cria um buffer com o tamanho da mensagem.
                                        if (await SocketHelper.ReadAsyncBytes(networkStream, pBuffer, 0, size, token))  //* ler a mensagem/pacote e armazena no buffer de pacote.
                                        {
                                            pBuffer = pBuffer.Decompress(); //* Descomprimir os dados recebidos.
                                            if (await SocketHelper.ReadAsyncBytes(networkStream, pIBuffer, 0, sizeof(short), token)) //* ler o pre-fixo(ID) do jogador, um inteiro, 4 bytes(sizeof(int)) e armazena no buffer.
                                            {
                                                int playerId = BitConverter.ToInt16(pIBuffer, 0); //* converte o buffer em inteiro.
                                                if (playerId <= Settings.GlobalSettings.MaxPlayers && playerId >= 0)
                                                {
                                                    NeutronPlayer player = Players[playerId];
                                                    //***************************************************************************************
                                                    NeutronPacket neutronPacket = Helper.CreatePacket(pBuffer, player, player, Protocol.Tcp);
                                                    //***************************************************************************************
                                                    _dataForProcessing.Add(neutronPacket, token);
#if UNITY_EDITOR
                                                    //* Adiciona no profiler a quantidade de dados de entrada(Incoming).
                                                    NeutronStatistics.ClientTCP.AddIncoming(size);
#endif
                                                }
                                                else
                                                    LogHelper.Error($"Player({playerId}) not found!!!!");
                                            }
                                            else
                                                Dispose(); //* Fecha a conexão do cliente.
                                        }
                                        else
                                            Dispose(); //* Fecha a conexão do cliente.
                                    }
                                    else
                                        LogHelper.Error($"Packet size exceeds defined limit!! size: {size}");
                                }
                                else
                                    Dispose(); //* Fecha a conexão do cliente.
                            }
                            break;
                        case Protocol.Udp:
                            {
                                //* precisa nem dizer nada aqui né?
                                switch (Constants.ReceiveAsyncPattern)
                                {
                                    case AsynchronousType.TAP:
                                        if (await SocketHelper.ReadAsyncBytes(UdpClient, StateObject))
                                            CreateUdpPacket();
                                        break;
                                    default:
                                        UdpApmReceive();
                                        whileOn = true;
                                        break;
                                }
                            }
                            break;
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (Exception ex)
            {
                LogHelper.StackTrace(ex);
            }
        }

        //* Aqui os pacotes serão processados, seus parâmetros serão lidos, e executado sua respectiva função.
        private void RunPacket(NeutronPlayer player, byte[] buffer)
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IReader reader = stream.Reader;
                //********************************************************
                reader.SetBuffer(buffer);
                //********************************************************
                Packet outPacket = (Packet)reader.ReadPacket();
                if (!IsReady && (outPacket != Packet.AuthStatus && outPacket != Packet.Handshake && outPacket != Packet.Disconnection && outPacket != Packet.Error))
                {
                    LogHelper.Error("Unauthenticated!");
                    return;
                }
                //********************************************************
                switch (outPacket)
                {
                    case Packet.UdpKeepAlive:
                        {
                            #region Reader
                            double serverTime = reader.ReadDouble();
                            double clientTime = reader.ReadDouble();
                            #endregion

                            #region Packet Loss
                            if (NetworkTime.Rpu < NetworkTime.Spu)
                                NetworkTime.Rpu++;
                            #endregion

                            #region Network Time/Statistics
                            NetworkTime.GetNetworkTime(clientTime, serverTime);
                            #endregion
                        }
                        break;
                    case Packet.Handshake:
                        {
                            #region Reader
                            double serverTime = reader.ReadDouble();
                            double clientTime = reader.ReadDouble();
                            int udpPort = reader.ReadInt();
                            NeutronPlayer localPlayer = reader.ReadIntExactly<NeutronPlayer>();
                            #endregion

                            #region Network Time/Statistics
                            NetworkTime.GetNetworkTime(clientTime, serverTime);
                            #endregion

                            #region Udp Syn/Ack
                            UdpEndPoint = new NonAllocEndPoint(IPAddress.Parse(_host), udpPort);
                            //* Udp, envia um pacote 30 vezes(30 vezes por que pode ocorrer perda de pacotes, e queremos que pelo menos 1 chegue ao servidor), para que seja feito o "Syn/Ack/Handshake"
                            using (NeutronStream ping = PooledNetworkStreams.Pull())
                            {
                                NeutronStream.IWriter writer = ping.Writer;
                                //*****************************************
                                for (int i = 0; i < 15; i++)
                                {
                                    writer.WritePacket((byte)Packet.UdpKeepAlive);
                                    writer.Write(NetworkTime.LocalTime);
                                    //***********************************
                                    Send(ping, Protocol.Udp);
                                }
                            }
                            #endregion

                            #region Logic
                            Player = localPlayer;
                            Players[Player.ID] = Player;
                            //******************************************************
                            OnPlayerConnected?.Invoke(Player, IsMine(Player), this);
                            #endregion
                        }
                        break;
                    case Packet.Disconnection:
                        {
                            #region Reader
                            string reason = reader.ReadString();
                            #endregion

                            #region Event
                            OnPlayerDisconnected?.Invoke(reason, player, IsMine(player), this);
                            #endregion
                        }
                        break;
                    case Packet.Chat:
                        {
                            #region Reader
                            string message = reader.ReadString();
                            #endregion

                            #region Event
                            OnMessageReceived?.Invoke(message, player, IsMine(player), this);
                            #endregion
                        }
                        break;
                    case Packet.iRPC:
                        {
                            #region Reader
                            RegisterMode registerType = (RegisterMode)reader.ReadPacket();
                            short viewID = reader.ReadShort();
                            byte rpcId = reader.ReadByte();
                            byte instanceId = reader.ReadByte();
                            byte[] parameters = reader.ReadNextBytes(buffer.Length);
                            #endregion

                            #region Logic
                            iRPCHandler(rpcId, viewID, instanceId, parameters, player, registerType);
                            #endregion
                        }
                        break;
                    case Packet.gRPC:
                        {
                            #region Reader
                            byte rpcId = reader.ReadByte();
                            byte[] parameters = reader.ReadNextBytes(buffer.Length);
                            #endregion

                            #region Logic
                            gRPCHandler(rpcId, player, parameters, false, IsMine(player));
                            #endregion
                        }
                        break;
                    case Packet.GetChannels:
                        {
                            #region Reader
                            NeutronChannel[] channels = reader.ReadIntExactly<NeutronChannel[]>();
                            #endregion

                            #region Logic
                            OnChannelsReceived?.Invoke(channels, this);
                            #endregion
                        }
                        break;
                    case Packet.JoinChannel:
                        {
                            #region Reader
                            NeutronChannel channel = reader.ReadIntExactly<NeutronChannel>();
                            #endregion

                            #region Logic
                            OnPlayerJoinedChannel?.Invoke(channel, player, IsMine(player), this);
                            #endregion
                        }
                        break;
                    case Packet.Leave:
                        {
                            #region Reader
                            MatchmakingMode packet = (MatchmakingMode)reader.ReadPacket();
                            #endregion

                            #region Logic
                            if (packet == MatchmakingMode.Channel)
                                OnPlayerLeftChannel?.Invoke(reader.ReadIntExactly<NeutronChannel>(), player, IsMine(player), this);
                            else if (packet == MatchmakingMode.Room)
                                OnPlayerLeftRoom?.Invoke(reader.ReadIntExactly<NeutronRoom>(), player, IsMine(player), this);
                            #endregion
                        }
                        break;
                    case Packet.CreateRoom:
                        {
                            #region Reader
                            NeutronRoom room = reader.ReadIntExactly<NeutronRoom>();
                            #endregion

                            #region Logic
                            OnPlayerCreatedRoom?.Invoke(room, player, IsMine(player), this);
                            #endregion
                        }
                        break;
                    case Packet.GetRooms:
                        {
                            #region Reader
                            NeutronRoom[] rooms = reader.ReadIntExactly<NeutronRoom[]>();
                            #endregion

                            #region Logic
                            OnRoomsReceived?.Invoke(rooms, this);
                            #endregion
                        }
                        break;
                    case Packet.JoinRoom:
                        {
                            #region Reader
                            NeutronRoom room = reader.ReadIntExactly<NeutronRoom>();
                            #endregion

                            #region Logic
                            OnPlayerJoinedRoom?.Invoke(room, player, IsMine(player), this);
                            #endregion
                        }
                        break;
                    case Packet.Destroy:
                        {
                            #region Logic
                            #endregion
                        }
                        break;
                    case Packet.Nickname:
                        {
                            #region Reader
                            string nickname = reader.ReadString();
                            #endregion

                            #region Logic
                            OnPlayerNicknameChanged?.Invoke(player, nickname, IsMine(player), this);
                            #endregion
                        }
                        break;
                    case Packet.SetPlayerProperties:
                        {
                            #region Reader
                            string properties = reader.ReadString();
                            #endregion

                            #region Logic
                            OnPlayerPropertiesChanged?.Invoke(player, properties, IsMine(player), this);
                            #endregion
                        }
                        break;
                    case Packet.SetRoomProperties:
                        {
                            #region Reader
                            string properties = reader.ReadString();
                            #endregion

                            #region Logic
                            OnRoomPropertiesChanged?.Invoke(player, properties, IsMine(player), this);
                            #endregion
                        }
                        break;
                    case Packet.CustomPacket:
                        {
                            #region Reader
                            byte packet = reader.ReadPacket();
                            byte[] parameters = reader.ReadIntExactly();
                            #endregion

                            #region Logic
                            using (NeutronStream cPStream = PooledNetworkStreams.Pull())
                            {
                                NeutronStream.IReader pReader = cPStream.Reader;
                                pReader.SetBuffer(parameters);
                                OnPlayerCustomPacketReceived?.Invoke(pReader, player, packet, this);
                            }
                            #endregion
                        }
                        break;
                    case Packet.AutoSync:
                        {
                            #region Reader
                            RegisterMode registerType = (RegisterMode)reader.ReadPacket();
                            short viewID = reader.ReadShort();
                            byte instanceId = reader.ReadByte();
                            byte[] parameters = reader.ReadNextBytes(buffer.Length);
                            #endregion

                            #region Logic
                            OnAutoSyncHandler(player, viewID, instanceId, parameters, registerType);
                            #endregion
                        }
                        break;
                    case Packet.AuthStatus:
                        {
                            #region Reader
                            string properties = reader.ReadString();
                            bool status = reader.ReadBool();
                            #endregion

                            #region Logic
                            OnNeutronAuthenticated?.Invoke(status, JObject.Parse(properties), this);
                            if (!status && !LogHelper.Error("The connection was rejected because authentication failed.")) { }
                            #endregion
                        }
                        break;
                    case Packet.Synchronize:
                        {
                            #region Reader
                            byte mode = reader.ReadByte();
                            #endregion

                            #region Logic
                            if (mode == 1) //* Atualiza os outros players para você.
                            {
                                NeutronPlayer[] othersPlayers = reader.ReadNextBytes(buffer.Length).Decompress(CompressionMode.Deflate).Deserialize<NeutronPlayer[]>();
                                //********************************************************************************************************************************
                                foreach (var currentPlayer in othersPlayers)
                                {
                                    if (currentPlayer.Equals(Player))
                                        continue;
                                    else
                                    {
                                        Players[currentPlayer.ID] = currentPlayer;
                                        //********************************************************************
                                        if (!currentPlayer.IsConnected)
                                            OnPlayerConnected?.Invoke(currentPlayer, IsMine(currentPlayer), this);
                                        if (Player.IsInChannel() && !currentPlayer.IsInChannel())
                                            OnPlayerJoinedChannel?.Invoke(Player.Channel, currentPlayer, IsMine(currentPlayer), this);
                                        if (Player.IsInRoom() && !currentPlayer.IsInRoom())
                                            OnPlayerJoinedRoom?.Invoke(Player.Room, currentPlayer, IsMine(currentPlayer), this);
                                    }
                                }
                            }
                            else if (mode == 2) //* Atualiza você para os outros players.
                            {
                                NeutronPlayer localPlayer = reader.ReadNextBytes(buffer.Length).Decompress(CompressionMode.Deflate).Deserialize<NeutronPlayer>();
                                //********************************************************************************************************************************
                                Players[localPlayer.ID] = localPlayer;
                                //****************************************************************
                                if (!localPlayer.IsConnected)
                                    OnPlayerConnected?.Invoke(localPlayer, IsMine(localPlayer), this);
                            }
                            #endregion
                        }
                        break;
                    case Packet.Error:
                        {
                            #region Reader
                            Packet packet = (Packet)reader.ReadPacket();
                            string message = reader.ReadString();
                            int errorCode = reader.ReadInt();
                            #endregion

                            #region Logic
                            OnError?.Invoke(packet, message, errorCode, this);
                            #endregion
                        }
                        break;
                }
            }
        }
        #endregion

        #region Methods -> Instance -> Packets
        //* Inicia uma pulsação para notificar que o cliente ainda está ativo.
        //* Se o servidor parar de receber esta pulsação o cliente será desconectado.
        private IEnumerator UdpKeepAlive()
        {
            yield return new WaitUntil(() => IsReady);
            //************************************************
            while (!TokenSource.Token.IsCancellationRequested)
            {
                NetworkTime.Spu++;
                //*****************//
                using (NeutronStream stream = PooledNetworkStreams.Pull())
                {
                    NeutronStream.IWriter writer = stream.Writer;
                    //********************************************
                    writer.WritePacket((byte)Packet.UdpKeepAlive);
                    writer.Write(NetworkTime.LocalTime);
                    //********************************************
                    Send(stream, Protocol.Udp);
                }
                //********************************
                yield return _yieldUdpKeepAlive;
            }
        }

        //* Inicia uma pulsação para notificar que o cliente ainda está ativo.
        private IEnumerator TcpKeepAlive()
        {
            yield return new WaitUntil(() => IsReady);
            //************************************************
            while (!TokenSource.Token.IsCancellationRequested)
            {
                using (NeutronStream stream = PooledNetworkStreams.Pull())
                {
                    NeutronStream.IWriter writer = stream.Writer;
                    //**********************************************
                    writer.WritePacket((byte)Packet.TcpKeepAlive);
                    //**********************************************
                    Send(stream, Protocol.Tcp);
                }
                //*******************************
                yield return _yieldTcpKeepAlive;
            }
        }

        /// <summary>
        ///* Sai da Sala, Canal ou Grupo.<br/>
        ///* A saída falhará se você não estiver em um canal ou sala.<br/>
        ///* Retorno de chamada: OnPlayerLeftChannel, OnPlayerLeftRoom, ou OnError.<br/>
        /// </summary>
        /// <param name="mode">* O tipo do pacote de saída.</param>
        /// <param name="player">* Attribuir este valor caso deseje enviar a partir do lado do servidor.</param>
        public void Leave(MatchmakingMode mode, NeutronPlayer player = null)
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //********************************************
                writer.WritePacket((byte)Packet.Leave);
                writer.WritePacket((byte)mode);
                //********************************************
                Send(stream, player, Protocol.Tcp);
            }
        }

        /// <summary>
        ///* Envia uma mensagem de Chat para o túnel especificado.<br/>
        ///* O envio falhará se a mensagem for em branco, nulo.<br/>
        ///* Retorno de chamada: OnMessageReceived ou OnError.<br/>
        /// </summary>
        /// <param name="message">* A mensagem que será enviada.</param>
        /// <param name="tunnelingTo">* O Túnel que será usado para a transmissão.</param>
        public void SendMessage(string message, TunnelingTo tunnelingTo)
        {
#if !UNITY_SERVER || UNITY_EDITOR
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //********************************************
                writer.WritePacket((byte)Packet.Chat);
                writer.WritePacket((byte)ChatMode.Global);
                writer.WritePacket((byte)tunnelingTo);
                writer.Write(message);
                //********************************************
                Send(stream, Protocol.Tcp);
            }
#else
            LogHelper.Error("This packet is not available on the server side.");
#endif
        }

        /// <summary>
        ///* Envia uma mensagem privada de chat para um jogador específico.<br/>
        ///* O envio falhará se o jogador especificado não existir ou se a mensagem for nula, em branco.<br/>
        ///* Retorno de chamada: OnMessageReceived ou OnError.<br/> 
        /// </summary>
        /// <param name="message">* A mensagem que será enviada.</param>
        /// <param name="player">* O jogador de destino da mensagem.</param>
        public void SendMessage(string message, NeutronPlayer player)
        {
#if !UNITY_SERVER || UNITY_EDITOR
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //********************************************
                writer.WritePacket((byte)Packet.Chat);
                writer.WritePacket((byte)ChatMode.Private);
                writer.Write(player.ID);
                writer.Write(message);
                //********************************************
                Send(stream, Protocol.Tcp);
            }
#else
            LogHelper.Error("This packet is not available on the server side.");
#endif
        }

        /// <summary>
        ///* Envia um pacote personalizado para a rede.<br/>
        ///* Retorno de chamada: OnPlayerPacketReceived ou OnError.<br/>
        /// </summary>
        /// <param name="parameters">* Os parâmetros que o pacote irá enviar.</param>
        /// <param name="packet">* O Pacote personalizado que será usado.</param>
        /// <param name="targetTo">* Define quais jogadores devem ser incluídos na lista de recepção do pacote.</param>
        /// <param name="tunnelingTo">* O Túnel que será usado para a transmissão.</param>
        /// <param name="recProtocol">* O protocolo que será usado para receber o pacote.</param>
        /// <param name="protocol">* O protocolo que será usado para enviar o pacote.</param>
        public void SendCustomPacket(NeutronStream.IWriter parameters, byte packet, TargetTo targetTo, TunnelingTo tunnelingTo, Protocol protocol)
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //********************************************
                writer.WritePacket((byte)Packet.CustomPacket);
                writer.Write(Player.ID);
                writer.WritePacket(packet);
                writer.WritePacket((byte)targetTo);
                writer.WritePacket((byte)tunnelingTo);
                writer.WriteIntWriter(parameters);
                //********************************************
                Send(stream, protocol);
            }
        }

        //* Invoca a chamada de Auto-Sincronização.
        [Network(PacketSize.AutoSync)]
        public void OnAutoSynchronization(NeutronStream stream, NeutronView view, byte instanceId, Protocol protocol, bool isServerSide = false)
        {
            NeutronStream.IWriter writer = stream.Writer;
            //********************************************
            if (writer.GetPosition() == 0)
            {
                writer.WritePacket((byte)Packet.AutoSync);
                writer.WritePacket((byte)view.RegisterMode);
                writer.Write((short)view.Id);
                writer.Write(instanceId);
                //********************************************
                Send(stream, view.Owner, isServerSide, protocol);
            }
            else
                LogHelper.Error($"You are called Finish() ?");
        }

        /// <summary>
        ///* gRPC(Global Remote Procedure Call), usado para a comunicação, isto é, a troca de dados ou sincronização via rede.<br/>
        ///* Prepara uma chamada gRPC na rede.
        /// </summary>
        /// <param name="stream">* Os parâmetros que serão enviados para o metódo a ser invocado.</param>
        [Network(PacketSize.gRPC)]
        public NeutronStream.IWriter Begin_gRPC(NeutronStream stream)
        {
            NeutronStream.IWriter writer = stream.Writer;
            //********************************************
            writer.SetPosition(PacketSize.gRPC);
            //************//
            return writer;
        }

        /// <summary>
        ///* gRPC(Global Remote Procedure Call), usado para a comunicação, isto é, a troca de dados ou sincronização via rede.
        /// </summary>
        /// <param name="id">* ID do metódo que será invocado.</param>
        /// <param name="parameters">* Os parâmetros que serão enviados para o metódo a ser invocado.</param>
        /// <param name="protocol">* O protocolo que será usado para enviar os dados.</param>
        /// <param name="player">* Attribuir este campo caso deseje enviar a partir do lado do servidor.</param>
#pragma warning disable IDE1006
        [Network(PacketSize.gRPC)]
        public void End_gRPC(byte id, NeutronStream stream, Protocol protocol, NeutronPlayer player = null)
#pragma warning restore IDE1006
        {
            NeutronStream.IWriter writer = stream.Writer;
            //********************************************
            if (writer.GetPosition() == 0)
            {
                writer.WritePacket((byte)Packet.gRPC);
                writer.Write(id);
                //********************************************
                Send(stream, player, protocol);
            }
            else
                LogHelper.Error($"You are called Finish() ?");
        }

        /// <summary>
        ///* iRPC(Instance Remote Procedure Call), usado para a comunicação, isto é, a troca de dados ou sincronização via rede.<br/>
        ///* Prepara uma chamada iRPC na rede.
        /// </summary>
        /// <param name="stream">* Os parâmetros que serão enviados para o metódo a ser invocado.</param>
        [Network(PacketSize.iRPC)]
        public NeutronStream.IWriter Begin_iRPC(NeutronStream stream)
        {
            NeutronStream.IWriter writer = stream.Writer;
            //********************************************
            writer.SetPosition(PacketSize.iRPC);
            //-------------//
            return writer;
        }

        /// <summary>
        ///* iRPC(Instance Remote Procedure Call), usado para a comunicação, isto é, a troca de dados ou sincronização via rede.
        /// </summary>
        /// <param name="rpcId">* ID do metódo que será invocado.</param>
        /// <param name="stream">* Os parâmetros que serão enviados para o metódo a ser invocado.</param>
        /// <param name="cache">* O Tipo de armazenamento em cache que será usado para guardar em cache.</param>
        /// <param name="targetTo">* Define quais jogadores devem ser incluídos na lista de recepção do pacote.</param>
        /// <param name="protocol">* O protocolo que será usado para enviar os dados.</param>
        [Network(PacketSize.iRPC)]
        public void End_iRPC(NeutronStream stream, NeutronView view, byte rpcId, byte instanceId, CacheMode cache, TargetTo targetTo, Protocol protocol, bool isServerSide = false)
        {
            NeutronStream.IWriter writer = stream.Writer;
            //********************************************
            if (writer.GetPosition() == 0)
            {
                writer.WritePacket((byte)Packet.iRPC);
                writer.WritePacket((byte)view.RegisterMode);
                writer.WritePacket((byte)targetTo);
                writer.WritePacket((byte)cache);
                writer.Write((short)view.Id);
                writer.Write(rpcId);
                writer.Write(instanceId);
                //***********************************************
                Send(stream, view.Owner, isServerSide, protocol);
            }
            else
                LogHelper.Error($"You are called Finish() ?");
        }

        /// <summary>
        ///* Registra um nickname para seu jogador.<br/>
        ///* O registro falhará se o nickname é em branco ou nulo ou igual ao anterior.<br/>
        ///* Retorno de chamada: OnPlayerNicknameChanged ou OnError.<br/>
        /// </summary>
        /// <param name="nickname">* O Nickname que você deseja registrar.</param>
        /// <param name="player">* Attribuir este valor caso deseje enviar a partir do lado do servidor.</param>
        public void SetNickname(string nickname, NeutronPlayer player = null)
        {
            if (!IsServer)
                Nickname = nickname;
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //********************************************
                writer.WritePacket((byte)Packet.Nickname);
                writer.Write(nickname);
                //********************************************
                Send(stream, player, Protocol.Tcp);
            }
        }

        /// <summary>
        ///* Ingressa em um canal pelo ID.<br/>
        ///* Se o ID for 0, ingressará em um canal aleatório.<br/>
        ///* A entrada em um canal falhará se o canal estiver cheio, fechado, não existente ou quando o usuário já estiver presente no canal.<br/>
        ///* Retorno de chamada: OnPlayerJoinedRoom ou OnError.<br/>
        /// </summary>
        /// <param name="channelId">* O ID do canal que deseja ingressar.</param>
        /// <param name="player">* Attribuir este valor caso deseje enviar a partir do lado do servidor.</param>
        public void JoinChannel(int channelId, NeutronPlayer player = null)
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //********************************************
                writer.WritePacket((byte)Packet.JoinChannel);
                writer.Write(channelId);
                //********************************************
                Send(stream, player, Protocol.Tcp);
            }
        }

        /// <summary>
        ///* Ingressa em uma sala pelo ID.<br/>
        ///* Se o ID for 0, ingressará em uma sala aleatória.<br/>
        ///* A entrada em uma sala falhará se a sala estiver cheia, fechada, não existente ou quando o usuário já estiver presente na sala.<br/>
        ///* Retorno de chamada: OnPlayerJoinedRoom ou OnError.<br/>
        /// </summary>
        /// <param name="roomId">* O ID da sala que deseja ingressar.</param>
        /// <param name="player">* Attribuir este valor caso deseje enviar a partir do lado do servidor.</param>
        public void JoinRoom(int roomId, string password, NeutronPlayer player = null)
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //********************************************
                writer.WritePacket((byte)Packet.JoinRoom);
                writer.Write(roomId);
                writer.Write(password ?? string.Empty);
                //********************************************
                Send(stream, player, Protocol.Tcp);
            }
        }

        /// <summary>
        ///* Cria uma nova sala.<br/>
        ///* A criação da sala falhará se o nome for nulo, em branco ou se a quantidade máxima de salas foi antigida.<br/>
        ///* Retorno de chamada: OnPlayerCreatedRoom ou OnError.<br/>
        /// </summary>
        /// <param name="player">* Attribuir este valor caso deseje enviar a partir do lado do servidor.</param>
        public void CreateRoom(NeutronRoom room, NeutronPlayer player = null)
        {
            room.Owner = Player;
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //********************************************
                writer.WritePacket((byte)Packet.CreateRoom);
                writer.Write(room.Password);
                writer.WriteIntExactly(room);
                //********************************************
                Send(stream, player, Protocol.Tcp);
            }
        }

        /// <summary>
        ///* Obtém os pacotes armazenados em cache.<br/>
        ///* Falhará se o ID especificado não for válido ou se não existir pacotes em cache.<br/>
        ///* Retorno de chamada: Nenhum.<br/>
        /// </summary>
        /// <param name="packet">* O tipo de pacote que deseja obter.</param>
        /// <param name="Id">* ID do pacote que deseja obter os dados.</param>
        /// <param name="includeOwnerPackets">* Define se você deve receber pacotes em cache que são seus.</param>
        /// <param name="player">* Attribuir este valor caso deseje enviar a partir do lado do servidor.</param>
        public void GetCache(CachedPacket packet = CachedPacket.All, byte Id = 0, bool includeOwnerPackets = true, NeutronPlayer player = null)
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //********************************************
                writer.WritePacket((byte)Packet.GetCache);
                writer.WritePacket((byte)packet);
                writer.Write(Id);
                writer.Write(includeOwnerPackets);
                //********************************************
                Send(stream, player, Protocol.Tcp);
            }
        }

        /// <summary>
        ///* Obtém os canais disponíveis.<br/>
        ///* Falhará se não houver canais disponíveis.<br/>
        ///* Retorno de chamada: OnChannelsReceived ou OnError.<br/>
        /// </summary>
        /// <param name="player">* Attribuir este valor caso deseje enviar a partir do lado do servidor.</param>
        public void GetChannels(NeutronPlayer player = null)
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //********************************************
                writer.WritePacket((byte)Packet.GetChannels);
                //********************************************
                Send(stream, player, Protocol.Tcp);
            }
        }

        /// <summary>
        ///* Obtém as salas disponíveis.<br/>
        ///* Falhará se não houver salas disponíveis.<br/>
        ///* Retorno de chamada: OnRoomsReceived ou OnError.<br/>
        /// </summary>
        /// <param name="player">* Attribuir este valor caso deseje enviar a partir do lado do servidor.</param>
        public void GetRooms(NeutronPlayer player = null)
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //********************************************
                writer.WritePacket((byte)Packet.GetRooms);
                //********************************************
                Send(stream, player, Protocol.Tcp);
            }
        }

        /// <summary>
        ///* Define as propriedades do seu jogador.<br/>
        ///* Retorno de chamada: OnPlayerPropertiesChanged ou OnError.<br/>
        /// </summary>
        /// <param name="properties">* O dicionário que contém as propriedades e valores a serem definidos para o jogador.</param>
        /// <param name="player">* Attribuir este valor caso deseje enviar a partir do lado do servidor.</param>
        public void SetPlayerProperties(Dictionary<string, object> properties, NeutronPlayer player = null)
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //****************************************************
                writer.WritePacket((byte)Packet.SetPlayerProperties);
                writer.Write(JsonConvert.SerializeObject(properties));
                //***************************************************
                Send(stream, player, Protocol.Tcp);
            }
        }

        /// <summary>
        ///* Define as propriedades da sala.<br/>
        ///* Retorno de chamada: OnRoomPropertiesChanged ou OnError.<br/>
        /// </summary>
        /// <param name="properties">* O dicionário que contém as propriedades e valores a serem definidos para o jogador.</param>
        /// <param name="player">* Attribuir este valor caso deseje enviar a partir do lado do servidor.</param>
        public void SetRoomProperties(Dictionary<string, object> properties, NeutronPlayer player = null)
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //***************************************************
                writer.WritePacket((byte)Packet.SetRoomProperties);
                writer.Write(JsonConvert.SerializeObject(properties));
                //***************************************************
                Send(stream, player, Protocol.Tcp);
            }
        }

        /// <summary>
        ///* Sincroniza o estado do seu jogador para os outros jogadores e os outros jogadores para você.<br/>
        /// </summary>
        /// <param name="protocol">* O protocolo que será usado para enviar os dados.</param>
        /// <param name="player">* Attribuir este valor caso deseje enviar a partir do lado do servidor.</param>
        public void Synchronize(Protocol protocol = Protocol.Tcp, NeutronPlayer player = null)
        {
            using (NeutronStream stream = PooledNetworkStreams.Pull())
            {
                NeutronStream.IWriter writer = stream.Writer;
                //***************************************************
                writer.WritePacket((byte)Packet.Synchronize);
                //***************************************************
                Send(stream, player, protocol);
            }
        }
        #endregion

        #region Methods -> Static
        /// <summary>
        ///* Cria uma instância de Neutron que será usada para realizar a conexão e a comunicação com o servidor.<br/>
        ///* As instâncias são independentes, cada instância é uma conexão nova.<br/>
        /// </summary>
        /// <param name="clientMode">* O tipo de cliente que a instância usará.</param>
        /// <returns>Retorna uma instância do tipo Neutron.</returns>
        public static Neutron Create(ClientMode clientMode = ClientMode.Player)
        {
            Neutron neutron = new Neutron();
            neutron.Initialize(clientMode);
#if UNITY_SERVER && !UNITY_EDITOR
            if (clientMode == ClientMode.Player)
            {
                LogHelper.Info($"The main player has been removed from the server build, but you can choose to use a virtual player!\r\n");
                return neutron;
            }
#endif
            if (neutron.PhysicsManager == null)
                neutron.PhysicsManager = SceneHelper.CreateContainer(neutron._sceneName, physics: Neutron.Server._localPhysicsMode);
            if (clientMode == ClientMode.Player)
            {
                if (Client == null)
                    Client = neutron;
                else
                    LogHelper.Error("The main player has already been initialized, you don't want to create a virtual client?");
            }
            return neutron;
        }

        /// <summary>
        ///* Instancia um objeto de rede.
        /// </summary>
        /// <param name="isServer">* Define se o lado a ser instaciado é o servidor ou o cliente.</param>
        /// <param name="prefab">* O Prefab que será usado para criar o objeto em rede.</param>
        /// <param name="position">* A posição que o objeto usará no momento de sua criação.</param>
        /// <param name="rotation">* A rotação que o objeto usará no momento de sua criação.</param>
        /// <returns>Retorna uma instância do tipo NeutronView.</returns>
        public static NeutronView Spawn(bool isServer, GameObject prefab, Vector3 position, Quaternion rotation)
        {
            NeutronView Spawn() => MonoBehaviour.Instantiate(prefab, position, rotation).GetComponent<NeutronView>();
            //* "Spawna" o objeto baseado no lado(Side) do NeutronView.
            if (prefab.TryGetComponent(out NeutronView neutronView))
            {
                switch (neutronView.Side)
                {
                    case Side.Both:
                        return Spawn();
                    case Side.Server:
                        return isServer ? Spawn() : null;
                    case Side.Client:
                        return !isServer ? Spawn() : null;
                    default:
                        return null;
                }
            }
            else
                return !LogHelper.Error("\"Neutron View\" object not found, failed to instantiate in network.") ? null : (NeutronView)null;
        }

        #region API Rest
        /// <summary>
        ///* Uma requisição POST é usado para enviar dados a um servidor para criar ou atualizar um recurso.
        /// </summary>
        /// <param name="url">O url no qual que será enviado a requisição.</param>
        /// <param name="formData">Os parâmetros que serão enviados para o metódo POST.</param>
        /// <param name="onAwake">Definido antes da requisição ser requisitada.</param>
        /// <param name="onResult">O resultado da requisição.</param>
        public static void Post(string url, Dictionary<string, string> formData, Action<UnityWebRequest> onAwake, Action<UnityWebRequest> onResult)
        {
            IEnumerator Request()
            {
                UnityWebRequest request = UnityWebRequest.Post(url, formData);
                onAwake.Invoke(request);
                yield return request.SendWebRequest();
                onResult.Invoke(request);
            }
            NeutronSchedule.ScheduleTask(Request());
        }

        /// <summary>
        ///* Uma requisição POST é usado para enviar dados a um servidor para criar ou atualizar um recurso.
        /// </summary>
        /// <param name="url">O url no qual que será enviado a requisição.</param>
        /// <param name="formData">Os parâmetros que serão enviados para o metódo POST.</param>
        /// <param name="onAwake">Definido antes da requisição ser requisitada.</param>
        /// <param name="onResult">O resultado da requisição.</param>
        public static Task PostAsync(string url, Dictionary<string, string> formData, Action<UnityWebRequest> onAwake, Action<UnityWebRequest> onResult)
        {
            TaskCompletionSource<bool> task = new TaskCompletionSource<bool>();
            IEnumerator Request()
            {
                UnityWebRequest request = UnityWebRequest.Post(url, formData);
                onAwake.Invoke(request);
                yield return request.SendWebRequest();
                onResult.Invoke(request);
                task.TrySetResult(true);
            }
            NeutronSchedule.ScheduleTask(Request());
            return task.Task;
        }

        /// <summary>
        ///* Uma requisição POST é usado para enviar dados a um servidor para criar ou atualizar um recurso.
        /// </summary>
        /// <param name="url">O url no qual que será enviado a requisição.</param>
        /// <param name="formData">Os parâmetros que serão enviados para o metódo POST.</param>
        /// <param name="onResult">O resultado da requisição.</param>
        public static void Post(string url, Dictionary<string, string> formData, Action<UnityWebRequest> onResult)
        {
            IEnumerator Request()
            {
                UnityWebRequest request = UnityWebRequest.Post(url, formData);
                yield return request.SendWebRequest();
                onResult.Invoke(request);
            }
            NeutronSchedule.ScheduleTask(Request());
        }

        /// <summary>
        ///* Uma requisição POST é usado para enviar dados a um servidor para criar ou atualizar um recurso.
        /// </summary>
        /// <param name="url">O url no qual que será enviado a requisição.</param>
        /// <param name="formData">Os parâmetros que serão enviados para o metódo POST.</param>
        /// <param name="onResult">O resultado da requisição.</param>
        public static Task PostAsync(string url, Dictionary<string, string> formData, Action<UnityWebRequest> onResult)
        {
            TaskCompletionSource<bool> task = new TaskCompletionSource<bool>();
            IEnumerator Request()
            {
                UnityWebRequest request = UnityWebRequest.Post(url, formData);
                yield return request.SendWebRequest();
                onResult.Invoke(request);
                task.TrySetResult(true);
            }
            NeutronSchedule.ScheduleTask(Request());
            return task.Task;
        }

        /// <summary>
        ///* A requisição GET é usado para solicitar dados de um recurso especificado. 
        /// </summary>
        /// <param name="url">O url no qual que será enviado a requisição.</param>
        /// <param name="onResult">O resultado da requisição.</param>
        public static void Get(string url, Action<UnityWebRequest> onResult)
        {
            IEnumerator Request()
            {
                UnityWebRequest request = UnityWebRequest.Get(url);
                yield return request.SendWebRequest();
                onResult.Invoke(request);
            }
            NeutronSchedule.ScheduleTask(Request());
        }

        /// <summary>
        ///* A requisição GET é usado para solicitar dados de um recurso especificado. 
        /// </summary>
        /// <param name="url">O url no qual que será enviado a requisição.</param>
        /// <param name="onResult">O resultado da requisição.</param>
        public static Task GetAsync(string url, Action<UnityWebRequest> onResult)
        {
            TaskCompletionSource<bool> task = new TaskCompletionSource<bool>();
            IEnumerator Request()
            {
                UnityWebRequest request = UnityWebRequest.Get(url);
                yield return request.SendWebRequest();
                onResult.Invoke(request);
                task.TrySetResult(true);
            }
            NeutronSchedule.ScheduleTask(Request());
            return task.Task;
        }

        /// <summary>
        ///* A requisição GET é usado para solicitar dados de um recurso especificado. 
        /// </summary>
        /// <param name="url">O url no qual que será enviado a requisição.</param>
        /// <param name="onAwake">Definido antes da requisição ser requisitada.</param>
        /// <param name="onResult">O resultado da requisição.</param>
        public static void Get(string url, Action<UnityWebRequest> onAwake, Action<UnityWebRequest> onResult)
        {
            IEnumerator Request()
            {
                UnityWebRequest request = UnityWebRequest.Get(url);
                onAwake.Invoke(request);
                yield return request.SendWebRequest();
                onResult.Invoke(request);
            }
            NeutronSchedule.ScheduleTask(Request());
        }

        /// <summary>
        ///* A requisição GET é usado para solicitar dados de um recurso especificado. 
        /// </summary>
        /// <param name="url">O url no qual que será enviado a requisição.</param>
        /// <param name="onAwake">Definido antes da requisição ser requisitada.</param>
        /// <param name="onResult">O resultado da requisição.</param>
        public static Task GetAsync(string url, Action<UnityWebRequest> onAwake, Action<UnityWebRequest> onResult)
        {
            TaskCompletionSource<bool> task = new TaskCompletionSource<bool>();
            IEnumerator Request()
            {
                UnityWebRequest request = UnityWebRequest.Get(url);
                onAwake.Invoke(request);
                yield return request.SendWebRequest();
                onResult.Invoke(request);
                task.TrySetResult(true);
            }
            NeutronSchedule.ScheduleTask(Request());
            return task.Task;
        }
        #endregion

        #endregion
    }
}