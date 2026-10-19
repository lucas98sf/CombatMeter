using System.Text;
using CombatMeter.Shared;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using ProjectM.UI;
using Unity.Collections;
using Unity.Entities;

namespace CombatMeter.Runtime;

// Native hooks stay in the permanent runtime, never in collectible plugins.
public static class MessageBridge
{
    public static event Action<EntityManager, Entity, string>? ServerReceived;
    public static event Action<string>? ClientReceived;

    internal static unsafe string Read(FixedString512Bytes text)
    {
        var size = text.utf8LengthInBytes;
        return size <= 509 ? Encoding.UTF8.GetString((byte*)&text + 2, size) : "";
    }
    internal static void Receive(EntityManager manager, bool server)
    {
        var query = manager.CreateEntityQuery(new ComponentType[] { server
            ? ComponentType.ReadOnly<ChatMessageEvent>() : ComponentType.ReadOnly<ChatMessageServerEvent>() });
        var entities = query.ToEntityArray(Allocator.Temp);
        try
        {
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var text = server ? Read(manager.GetComponentData<ChatMessageEvent>(entity).MessageText)
                    : Read(manager.GetComponentData<ChatMessageServerEvent>(entity).MessageText);
                if (!text.StartsWith(WireCodec.Prefix, StringComparison.Ordinal)) continue;
                // Consume our channel before native chat routing/display; never broadcast requests.
                if (server)
                {
                    if (!manager.HasComponent<FromCharacter>(entity)) continue;
                    var sender = manager.GetComponentData<FromCharacter>(entity).User;
                    manager.DestroyEntity(entity);
                    if (manager.Exists(sender) && manager.HasComponent<User>(sender)
                        && manager.GetComponentData<User>(sender).IsConnected)
                        ServerReceived?.Invoke(manager, sender, text);
                }
                else
                {
                    var message = manager.GetComponentData<ChatMessageServerEvent>(entity);
                    manager.DestroyEntity(entity);
                    // Players cannot supply server system messages. Whisper echoes are only hidden.
                    if (message.MessageType == ServerChatMessageType.System) ClientReceived?.Invoke(text);
                }
            }
        }
        finally { entities.Dispose(); query.Dispose(); }
    }
    internal static void ReceiveSafely(EntityManager manager, bool server)
    {
        try { Receive(manager, server); }
        catch (Exception ex) { RuntimeHost.Instance.Log.LogWarning($"CombatMeter native message delivery failed: {ex.Message}"); }
    }
    public static void SendClient(World world, Entity user, string frame)
    {
        var manager = world.EntityManager;
        var entity = manager.CreateEntity(new ComponentType[] { ComponentType.ReadWrite<ChatMessageEvent>(),
            ComponentType.ReadWrite<SendNetworkEventTag>(), ComponentType.ReadWrite<NetworkEventType>() });
        manager.SetComponentData(entity, new ChatMessageEvent { MessageType = ChatMessageType.Whisper,
            ReceiverEntity = manager.GetComponentData<NetworkId>(user), MessageText = new FixedString512Bytes(frame) });
        manager.SetComponentData(entity, new NetworkEventType { EventId = NetworkEvents.EventId_ChatMessageEvent });
    }
    public static void SendServer(EntityManager manager, User user, string frame)
    {
        var text = new FixedString512Bytes(frame);
        ServerChatUtils.SendSystemMessageToClient(manager, user, ref text);
    }
}

[HarmonyPatch(typeof(ChatMessageSystem), nameof(ChatMessageSystem.OnUpdate))]
internal static class ServerMessagePatch
{
    private static void Prefix(ChatMessageSystem __instance) => MessageBridge.ReceiveSafely(__instance.EntityManager, true);
}
[HarmonyPatch(typeof(ClientChatSystem), nameof(ClientChatSystem.OnUpdate))]
internal static class ClientMessagePatch
{
    private static void Prefix(ClientChatSystem __instance) => MessageBridge.ReceiveSafely(__instance.EntityManager, false);
}
