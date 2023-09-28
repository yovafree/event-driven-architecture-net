using Microsoft.AspNetCore.Mvc;
using publisher.Dtos;
using publisher.RabbitMQ;

namespace publisher.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly IMessageProducer _messagePublisher;

        public OrdersController(IMessageProducer messagePublisher)
        {
            _messagePublisher = messagePublisher;
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder(OrderDto orderDto)
        {
            _messagePublisher.SendMessage(orderDto);

            return Ok(new { id = orderDto.Id });
        }
    }
}