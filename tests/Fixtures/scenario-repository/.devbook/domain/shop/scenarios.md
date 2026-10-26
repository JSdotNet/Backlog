# Scenarios

```meta
type: scenarios
```

## Place an order

```meta
type: scenario
start: /shop
```

A shopper fills a cart and places the order. The end-to-end tests read this journey as
a repository's scenario: its committed run passed the first part and failed the second
on its first step.

### The cart is filled

- **Given** an empty cart
- **When** I add "Blue mug" to the cart
- **Then** the cart holds one item

![The filled cart](shot:cart-filled)

### The order is placed

- **When** I place the order
- **Then** the order confirmation shows

![The confirmation](shot:order-confirmed)
