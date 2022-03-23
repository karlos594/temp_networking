//using System;
//using System.Collections.Generic;
//using System.Text;

//namespace DarkGalaxy_Networking
//{
//    public static class NetworkMessageBuilder
//    {
//        private static Dictionary<short, Type> _messages = new Dictionary<short, Type>();
//        private static Dictionary<Type, short> _messagesByType = new Dictionary<Type, short>();

//        static NetworkMessageBuilder()
//        {
//            var messageType = typeof(INetworkMessage);

//            short count = 0;
//            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
//            foreach(var assembly in assemblies)
//            {
//                var types = assembly.GetTypes();
//                foreach(var type in types)
//                {
//                    if(messageType.IsAssignableFrom(type) && !type.IsInterface && !_messagesByType.ContainsKey(type))//type.GetInterfaces().Contains(typeof(INetworkMessage))
//                    {
//                        count++;

//                        _messages.Add(count, type);
//                        _messagesByType.Add(type, count);
//                    }
//                }
//            }
//        }

//        public static short GetMessageIDByType(Type messageType)
//        {
//            short messageID;
//            bool contains = _messagesByType.TryGetValue(messageType, out messageID);

//            return (contains ? messageID : (short)-1);
//        }
//        public static INetworkMessage GetMessageInstance(short messageID)
//        {
//            Type message;
//            bool contains = _messages.TryGetValue(messageID, out message);

//            return (contains ? ((INetworkMessage)Activator.CreateInstance(message)) : null);
//        }
//    }
//}
