using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventBus.Base;

// Represents information about a subscription to an event.
// Contains the type of the event handler that will handle the event.
public class SubscriptionInfo
{
    public Type HandlerType { get; }

    public SubscriptionInfo(Type handlerType)
    {
        HandlerType = handlerType ?? throw new ArgumentNullException(nameof(handlerType));
    }
    public static SubscriptionInfo Typed(Type handlerType)
    {
        return new SubscriptionInfo(handlerType);
    }
}
