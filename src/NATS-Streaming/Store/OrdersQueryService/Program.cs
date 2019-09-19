﻿using System;
using System.Reflection;
using System.Text.Json;
using Store.Messaging;
using Store.Messaging.Events;
using System.Linq;
using System.Text;

namespace Store.QueryService
{
    class Program
    {
        private static STANMessageBroker _eventsMessageBroker;
        private static NATSMessageBroker _queriesMessageBroker;

        static void Main(string[] args)
        {
            _eventsMessageBroker = new STANMessageBroker("nats://localhost:4223", "OrdersQueryService");
            _eventsMessageBroker.StartDurableMessageConsumer("store.events", EventReceived, "OrdersQueryService");

            _queriesMessageBroker = new NATSMessageBroker("nats://localhost:4222");
            _queriesMessageBroker.StartMessageConsumer("store.queries.*", QueryReceived);

            Console.Clear();
            Console.WriteLine("OrdersQueryService inline.");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);

            _eventsMessageBroker.StopMessageConsumers();
            _eventsMessageBroker.Dispose();
            _queriesMessageBroker.StopMessageConsumers();
            _queriesMessageBroker.Dispose();
        }

        private static void EventReceived(string messageType, string messageData, ulong sequenceNumber)
        {
            try
            {
                Type eventType = Type.GetType(messageType);
                dynamic e = JsonSerializer.Deserialize(messageData, eventType);
                Handle(e);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Error: {ex.InnerException.Message}");
                }
            }
        }

        private static MethodInfo DetermineHandlerMethod(string messageType)
        {
            return typeof(Store.QueryService.Program)
                .GetMethod(messageType, BindingFlags.NonPublic | BindingFlags.Static);
        }

        private static async void Handle(OrderCreated e)
        {
            Console.WriteLine($"Order #{e.OrderNumber} created.");

            using (var dbContext = new StoreDBContext())
            {
                dbContext.Orders.Add(new Order { OrderNumber = e.OrderNumber, Status = "In progress" });
                await dbContext.SaveChangesAsync();
            }
        }

        private static async void Handle(ProductOrdered e)
        {
            Console.WriteLine($"Product #{e.ProductNumber} added to order #{e.OrderNumber}.");

            using (var dbContext = new StoreDBContext())
            {
                var order = dbContext.Orders.FirstOrDefault(o => o.OrderNumber == e.OrderNumber);
                if (order != null)
                {
                    order.Products.Add(new OrderedProduct
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProductNumber = e.ProductNumber,
                        Price = e.Price
                    });
                    order.TotalPrice += e.Price;
                    await dbContext.SaveChangesAsync();
                }
            }
        }

        private static async void Handle(ProductRemoved e)
        {
            Console.WriteLine($"Product #{e.ProductNumber} removed from order #{e.OrderNumber}.");

            using (var dbContext = new StoreDBContext())
            {
                var order = dbContext.Orders.FirstOrDefault(o => o.OrderNumber == e.OrderNumber);
                if (order != null)
                {
                    dbContext.Entry(order).Collection(o => o.Products).Load();
                    var product = order.Products.FirstOrDefault(p => p.ProductNumber == e.ProductNumber);
                    if (product != null)
                    {
                        order.Products.Remove(product);
                        order.TotalPrice -= product.Price;
                        await dbContext.SaveChangesAsync();
                    }
                }
            }
        }

        private static async void Handle(OrderCompleted e)
        {
            Console.WriteLine($"Order #{e.OrderNumber} completed.");

            using (var dbContext = new StoreDBContext())
            {
                var order = dbContext.Orders.FirstOrDefault(o => o.OrderNumber == e.OrderNumber);
                if (order != null)
                {
                    order.ShippingAddress = e.ShippingAddress;
                    order.Status = "Completed";
                    await dbContext.SaveChangesAsync();
                }
            }
        }

        private static async void Handle(OrderShipped e)
        {
            Console.WriteLine($"Order #{e.OrderNumber} shipped.");

            using (var dbContext = new StoreDBContext())
            {
                var order = dbContext.Orders.FirstOrDefault(o => o.OrderNumber == e.OrderNumber);
                if (order != null)
                {
                    order.Status = "Shipped";
                    await dbContext.SaveChangesAsync();
                }
            }
        }

        private static async void Handle(OrderCancelled e)
        {
            Console.WriteLine($"Order #{e.OrderNumber} cancelled.");

            using (var dbContext = new StoreDBContext())
            {
                var order = dbContext.Orders.FirstOrDefault(o => o.OrderNumber == e.OrderNumber);
                if (order != null)
                {
                    dbContext.Orders.Remove(order);
                    await dbContext.SaveChangesAsync();
                }
            }
        }

        private static string QueryReceived(string messageType, string messageData)
        {
            Console.WriteLine("Query received.");

            // messageType is ignored for now - only 1 query supported

            try
            {
                using (var dbContext = new StoreDBContext())
                {
                    StringBuilder ordersList = new StringBuilder($"Order#\t| Status\t| Total amount\n");
                    ordersList.AppendLine($"--------|---------------|----------------");
                    foreach (Order order in dbContext.Orders)
                    {
                        ordersList.AppendLine($"{order.OrderNumber}\t| {order.Status}\t| {order.TotalPrice}");
                    }
                    return ordersList.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Error: {ex.InnerException.Message}");
                }
            }

            return "Error";
        }
    }
}
