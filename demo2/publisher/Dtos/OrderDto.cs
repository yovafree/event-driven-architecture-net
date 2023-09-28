namespace publisher.Dtos
{
    public class OrderDto
    {
        public string Id {get;set;}
        public string ProductName { get; set; }

        public decimal Price { get; set; }

        public int Quantity { get; set; }
    }
}