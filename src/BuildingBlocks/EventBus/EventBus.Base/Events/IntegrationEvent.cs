using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventBus.Base.Events;

//This class represents a base class that  is conducter to other services via rabbitMQ or AzureServiceBus
public class IntegrationEvent
{
    [JsonProperty]
    public Guid Id { get; private set; }
    [JsonProperty]
    public DateTime CreatedDate { get; private set; }

    [JsonConstructor]
    public IntegrationEvent()
    {
        Id = Guid.NewGuid();
        CreatedDate = DateTime.UtcNow;
    }

    [JsonConstructor]
    public IntegrationEvent(Guid ıd, DateTime createdDate)
    {
        Id = ıd;
        CreatedDate = createdDate;
    }
}
