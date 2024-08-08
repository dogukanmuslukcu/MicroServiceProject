using EventBus.Base;
using EventBus.Base.Events;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Management;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SellingBuddy.EventBus.Base.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventBus.AzureServiceBus;
// Implements an EventBus using Azure Service Bus.
// Manages event publishing and subscription for the Service Bus, including topic and subscription management.
public class EventBusServiceBus : BaseEventBus
{
    private ITopicClient topicClient;
    private ManagementClient managementClient;
    private ILogger logger;

    public EventBusServiceBus(IServiceProvider serviceProvider, EventBusConfiguration eventBusConfig) : base(serviceProvider, eventBusConfig)
    {
        managementClient = new ManagementClient(eventBusConfig.EventBusConnectionString);
        topicClient = CreateTopicClient();
        logger = (ILogger<EventBusServiceBus>)serviceProvider.GetService(typeof(ILogger<EventBusServiceBus>));
    }
    private ITopicClient CreateTopicClient()
    {
        if (topicClient == null || topicClient.IsClosedOrClosing)
        {
            topicClient = new TopicClient(EventBusConfig.EventBusConnectionString, EventBusConfig.DefaultTopicName, RetryPolicy.Default);
        }
        //Ensure that topic already exists
        if (!managementClient.TopicExistsAsync(EventBusConfig.DefaultTopicName).GetAwaiter().GetResult())
        {
            managementClient.CreateTopicAsync(EventBusConfig.DefaultTopicName).GetAwaiter().GetResult();
        }
        return topicClient;
    }
    public override void Publish(IntegrationEvent @event)
    {
        var eventName = @event.GetType().Name; // example :OrderCreatedIntegrationEvent
        eventName = ProcessEventName(eventName); // example : OrderCreated

        var eventStr = JsonConvert.SerializeObject(@event);
        var bodyArr = Encoding.UTF8.GetBytes(eventStr);

        var message = new Message()
        {
            MessageId = Guid.NewGuid().ToString(),
            Body = bodyArr,
            Label = eventName
        };

        topicClient.SendAsync(message).GetAwaiter().GetResult();
    }
    public override void Subscribe<T, TH>()
    {
        var eventName = typeof(T).Name;
        eventName = ProcessEventName(eventName);

        if (!SubscriptionManager.HasSubscriptionForEvent(eventName))
        {
            var subscriptionClient = CreateSubscriptionClient(eventName);
            RegisterSubscriptionClientMessageHandler(subscriptionClient);
        }

        logger.LogInformation("Subscribing to event {eventName} with {EventHandler}", eventName, typeof(TH).Name);
        SubscriptionManager.AddSubscription<T, TH>();
    }
    private void RegisterSubscriptionClientMessageHandler(ISubscriptionClient subscriptionClient)
    {
        subscriptionClient.RegisterMessageHandler(async (message, token) =>
        {
            var eventName = $"{message.Label}";
            var messageData = Encoding.UTF8.GetString(message.Body);

            //Complete the message so that it is not received again.
            if (await ProcessEvent(ProcessEventName(eventName), messageData))
            {
                await subscriptionClient.CompleteAsync(message.SystemProperties.LockToken);
            }
        },
        new MessageHandlerOptions(ExceptionReceivedHandler)
        {
            MaxConcurrentCalls = 10,
            AutoComplete = false
        });

    }
    private async Task ExceptionReceivedHandler(ExceptionReceivedEventArgs args)
    {
        Console.WriteLine($"Message handler encountered an exception {args.Exception}.");
        await Task.CompletedTask;
    }
    private SubscriptionClient CreateSubscriptionClient(string eventName)
    {
        return new SubscriptionClient
            (
                EventBusConfig.EventBusConnectionString,
                EventBusConfig.DefaultTopicName,
                GetSubName(eventName)
            );
    }
    private ISubscriptionClient CreateSubscriptionClientIfNotExists(string eventName)
    {
        var subscriptionClient = CreateSubscriptionClient(eventName);

        var exists = managementClient.SubscriptionExistsAsync(EventBusConfig.DefaultTopicName, GetSubName(eventName)).GetAwaiter().GetResult();

        if (!exists)
        {
            managementClient.CreateSubscriptionAsync(EventBusConfig.DefaultTopicName, GetSubName(eventName)).GetAwaiter().GetResult();
            RemoveDefaultRule(subscriptionClient);
        }
        CreateRuleIfNotExists(ProcessEventName(eventName), subscriptionClient);
        return subscriptionClient;
    }
    private void CreateRuleIfNotExists(string eventName, ISubscriptionClient subscriptionClient)
    {
        bool ruleExists;
        try
        {
            var rule = managementClient.GetRuleAsync(EventBusConfig.DefaultTopicName, eventName, eventName).GetAwaiter().GetResult();
            ruleExists = rule != null;
        }
        catch (MessagingEntityNotFoundException)
        {
            //Azure Management Client doesn't have RuleExists method
            ruleExists = false;
        }
        if (!ruleExists)
        {
            subscriptionClient.AddRuleAsync(new RuleDescription()
            {
                Filter = new CorrelationFilter()
                {
                    Label = eventName
                },
                Name = eventName
            }).GetAwaiter().GetResult();
        }
    }
    private void RemoveDefaultRule(SubscriptionClient subscriptionClient)
    {
        try
        {
            subscriptionClient.RemoveRuleAsync(RuleDescription.DefaultRuleName).GetAwaiter().GetResult();
        }
        catch (MessagingEntityNotFoundException)
        {

            logger.LogWarning("The messaging entity {DefaultRuleName} Could not be found.", RuleDescription.DefaultRuleName);
        }
    }
    public override void UnSubscribe<T, TH>()
    {
        var eventName = typeof(T).Name;
        try
        {
            //Subscription will be there but we don't subscribe
            var subscriptionClient = CreateSubscriptionClient(eventName);
            subscriptionClient
                .RemoveRuleAsync(eventName)
                .GetAwaiter()
                .GetResult();
        }
        catch (MessagingEntityNotFoundException)
        {
            logger.LogWarning("The messaging entity {eventName} Could not be found.", eventName);
        }

        logger.LogInformation("Unsubscribing from event {EventName}", eventName);

        SubscriptionManager.RemoveSubscription<T, TH>();
    }
    public override void Dispose()
    {
        base.Dispose();

        topicClient.CloseAsync().GetAwaiter().GetResult();
        topicClient = null;

        managementClient.CloseAsync().GetAwaiter().GetResult();
        managementClient = null;
    }
}
