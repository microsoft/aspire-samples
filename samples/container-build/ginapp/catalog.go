package main

var onOff = map[string]any{"on": true, "off": false}

// seedFlags is the Contoso Outfitters storefront's flag catalog, loaded at startup and by the "Reset flags" command.
var seedFlags = []Flag{
	{
		Key:            "checkout.express-pay",
		Description:    "Show Apple Pay and Google Pay express buttons on the cart page.",
		Enabled:        true,
		Variants:       onOff,
		DefaultVariant: "off",
		OffVariant:     "off",
		Rollout:        []Allocation{{Variant: "on", Weight: 10}, {Variant: "off", Weight: 90}},
	},
	{
		Key:            "search.semantic-ranking",
		Description:    "Rank product search results with the semantic relevance model.",
		Enabled:        true,
		Variants:       onOff,
		DefaultVariant: "off",
		OffVariant:     "off",
		Rules:          []TargetingRule{{Attribute: "plan", Values: []string{"premium"}, Variant: "on"}},
		Rollout:        []Allocation{{Variant: "on", Weight: 30}, {Variant: "off", Weight: 70}},
	},
	{
		Key:         "catalog.recommendations",
		Description: "Strategy behind the 'You may also like' carousel (A/B/n experiment).",
		Enabled:     true,
		Variants: map[string]any{
			"bestsellers":   "bestsellers",
			"collaborative": "collaborative-filtering",
			"trending":      "trending-near-you",
		},
		DefaultVariant: "bestsellers",
		OffVariant:     "bestsellers",
		Rollout: []Allocation{
			{Variant: "bestsellers", Weight: 50},
			{Variant: "collaborative", Weight: 30},
			{Variant: "trending", Weight: 20},
		},
	},
	{
		Key:            "loyalty.points-multiplier",
		Description:    "Trailblazer Rewards points multiplier for the winter promotion.",
		Enabled:        true,
		Variants:       map[string]any{"standard": 1.0, "double": 2.0, "triple": 3.0},
		DefaultVariant: "standard",
		OffVariant:     "standard",
		Rules: []TargetingRule{
			{Attribute: "plan", Values: []string{"premium"}, Variant: "triple"},
			{Attribute: "country", Values: []string{"US", "CA"}, Variant: "double"},
		},
	},
	{
		Key:            "shipping.free-threshold-usd",
		Description:    "Order subtotal in USD that qualifies for free standard shipping.",
		Enabled:        true,
		Variants:       map[string]any{"standard": 75.0, "holiday": 49.0},
		DefaultVariant: "holiday",
		OffVariant:     "standard",
	},
	{
		Key:            "storefront.winter-sale-banner",
		Description:    "Show the winter sale hero banner on the home page. Scheduled for December 1.",
		Enabled:        false,
		Variants:       onOff,
		DefaultVariant: "on",
		OffVariant:     "off",
	},
	{
		Key:            "payments.fraud-screening-v3",
		Description:    "Route card payments through the v3 fraud-screening model.",
		Enabled:        true,
		Variants:       onOff,
		DefaultVariant: "off",
		OffVariant:     "off",
		Rollout:        []Allocation{{Variant: "on", Weight: 50}, {Variant: "off", Weight: 50}},
	},
	{
		Key:            "mobile.ar-tent-preview",
		Description:    "Augmented-reality tent preview in the iOS and Android apps.",
		Enabled:        true,
		Variants:       onOff,
		DefaultVariant: "off",
		OffVariant:     "off",
		Rules:          []TargetingRule{{Attribute: "platform", Values: []string{"ios", "android"}, Variant: "on"}},
	},
}
