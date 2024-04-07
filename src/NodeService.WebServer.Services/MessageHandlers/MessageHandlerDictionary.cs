using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NodeService.WebServer.Services.MessageHandlers
{
    public class MessageHandlerDictionary : Dictionary<MessageDescriptor, IMessageHandler>
    {
        public MessageHandlerDictionary()
        {

        }

    }
}
