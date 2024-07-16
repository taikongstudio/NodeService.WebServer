using Google.Protobuf.Reflection;

namespace NodeService.WebServer.Services.NodeSessions.MessageHandlers;

public class MessageHandlerDictionary : Dictionary<MessageDescriptor, IMessageHandler>
{
}