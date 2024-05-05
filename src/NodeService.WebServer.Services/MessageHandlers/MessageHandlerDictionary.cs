using Google.Protobuf.Reflection;

namespace NodeService.WebServer.Services.MessageHandlers;

public class MessageHandlerDictionary : Dictionary<MessageDescriptor, IMessageHandler>
{
}