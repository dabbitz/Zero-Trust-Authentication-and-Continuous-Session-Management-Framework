using CommunityToolkit.Mvvm.Messaging.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace ZeroTrustClient
{
    public class AuthFinishedMessage : ValueChangedMessage<bool>
    {
        public AuthFinishedMessage(bool value) : base(value) { }
    }
}
