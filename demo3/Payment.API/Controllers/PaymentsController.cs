using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Payment.API.Models;
using Payment.API.Models.DTOs;
using RabbitMQ.Client;

namespace Payment.API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PaymentsController : ControllerBase
    {
        private static readonly ActivitySource Activity = new(nameof(ControllerBase));
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;
        private readonly ILogger<PaymentsController> _logger;
        private DemoContext _context;
        private readonly IConfiguration _configuration;
        public PaymentsController(ILogger<PaymentsController> logger, DemoContext context, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _context = context;
        }

        [HttpPost("pay")]
        public string Pay([FromBody]OrderDto order)
        {
            using (var activity = Activity.StartActivity("Se recibe confirmación de pago", ActivityKind.Server))
            {
                 _logger.LogInformation("Orden en confirmación #" + order.OrderUuid);

                if (OrderExist(order.OrderUuid)){
                    _logger.LogInformation($"Orden #{order.OrderUuid}, ya fue confirmada.");
                    return $"Orden #{order.OrderUuid}, ya fue confirmada.";
                }

                Order item = new Order();
                item.ClientName = order.ClientName;
                item.Address = order.Address;
                item.Total = order.Total;
                item.CreationDate = order.CreationDate;
                item.UuidTransaction = order.OrderUuid;
                item.PaymentGateway = "VISA";
                item.OrderDetails = order.Details.Select(x => new OrderDetail(){
                    Price = x.Price,
                    Quantity = x.Quantity,
                    Product = x.Product,
                    Subtotal = x.Subtotal,
                    OrderId = item.OrderId
                }).ToList();

                _context.Orders.Add(item);
                _context.SaveChanges();
                string msj = $"Orden #{order.OrderUuid}. Se confirma pago por medio de pasarela.";

                NotificationDto notification = new NotificationDto();
                notification.Type = "Finish";
                notification.Message = msj;
                notification.OrderUuid = item.UuidTransaction;
                notification.Date = DateTime.Now;

                SendQueueHistory(notification);
                _logger.LogInformation(msj);
            }
            return $"Orden #{order.OrderUuid} procesada.";
        }


        [HttpGet("order/{id}")]
        public OrderDto2 GetOrder(string id)
        {
            using (var activity = Activity.StartActivity("Solicitan Información de Orden", ActivityKind.Producer))
            {
                var item = _context.Orders.Where(x => x.UuidTransaction == id).FirstOrDefault();

                if (item == null){
                    _logger.LogInformation($"Orden #{id} no existe.");
                    return null;
                }

                var details = _context.OrderDetails.Where(x => x.OrderId == item.OrderId).ToList();

                OrderDto2 reg = new OrderDto2(){
                    OrderID = item.OrderId,
                    ClientName = item.ClientName,
                    CreationDate = item.CreationDate,
                    Address = item.Address,
                    Total = item.Total,
                    UuidTransaction = item.UuidTransaction,
                    PaymentGateway = item.PaymentGateway,
                    Details = details.Select(x => new OrderDetailDto(){
                        Quantity=x.Quantity,
                        Product=x.Product,
                        Price=x.Price,
                        Subtotal=x.Subtotal
                    }).ToList()
                };

            return reg;
            }
            
        }

        private bool OrderExist(string uuid){

            var item = _context.Orders.Where(x => x.UuidTransaction == uuid).FirstOrDefault();

            return item != null ? true : false;
        }

        
        private void SendQueueHistory(NotificationDto item){
            try
            {
                using (var activity = Activity.StartActivity("Publicando a RabbitMQ", ActivityKind.Producer))
                {
                    var factory = new ConnectionFactory { HostName = _configuration["RabbitMq:Hostname"] };
                    using (var connection = factory.CreateConnection())
                    using (var channel = connection.CreateModel())
                    {
                        var props = channel.CreateBasicProperties();

                        //AddActivityToHeader(activity, props);

                        channel.QueueDeclare(queue: _configuration["RabbitMq:QueueName2"],
                            durable: false,
                            exclusive: false,
                            autoDelete: false,
                            arguments: null);

                        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item));

                        _logger.LogInformation($"Publicando mensaje a cola: {_configuration["RabbitMq:QueueName2"]}");
                        channel.BasicPublish(exchange: "",
                            routingKey: _configuration["RabbitMq:QueueName2"],
                            basicProperties: props,
                            body: body);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError("Error al publicar un mensaje en cola.", e);
                throw;
            }
        }

        private void AddActivityToHeader(Activity activity, IBasicProperties props)
        {
            Propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), props, InjectContextIntoHeader);
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.destination_kind", "queue");
            activity?.SetTag("messaging.rabbitmq.queue", _configuration["RabbitMq:QueueName2"]);
        }

        private void InjectContextIntoHeader(IBasicProperties props, string key, string value)
        {
            try
            {
                props.Headers ??= new Dictionary<string, object>();
                props.Headers[key] = value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to inject trace context.");
            }
        }
    }
}