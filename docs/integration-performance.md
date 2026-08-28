# OmnisVigil integration performance

When you point OmnisRouter at an OmnisVigil collector, two endpoints carry the traffic between them: receipts go up, policy comes down. This note records what to expect from those two under load, so you can size a deployment and understand the cost of polling before you turn it on. It covers only the router-to-collector contract; the rest of OmnisVigil is not the router's concern.

The figures come from a k6 run against a single collector instance. Read them as a floor. The client and the collector shared one machine, so there is no network latency in these numbers, and the latency figures are from one instance. The collector also runs as a fleet behind a load balancer, which is covered at the end.

## Receipts up: `POST /ingest/v1/receipts`

The router batches content-free receipts and ships them here.

- A single collector instance took records at a p95 of **40ms** per request with no failures, from a 15-user ramp posting single-record batches. The fold into rollups happens on a background worker, off the ingest path, so a slow fold never holds your push open.
- Your router batches many records per request, so the effective record rate in practice is higher than that, well past a design point of around 500,000 records per day per tenant on one instance.
- Reporting is fail-open. If the collector is slow or briefly unreachable, the router keeps routing and reships later, so ingest latency never blocks a request. Receipts are de-duplicated by their id, so reshipping after an outage is safe.

## Policy down: `GET /policy/v1/current`

The router polls here for the current caps, allowed models, and kill state.

- The response carries an ETag. Send it back as `If-None-Match`, and an unchanged policy comes back **304 Not Modified** with no body.
- In the test, **21,470 of 21,490 polls were 304**; only the first poll per client was a full 200. Poll p95 was **56ms**, including those occasional full responses.
- So polling once a minute across a large fleet costs the collector very little. Enforcement is local and fail-safe: the router keeps enforcing the last known policy if the collector is briefly unreachable, so a poll that fails never opens the gate.

## Running the collector highly available

The collector holds no state of its own, so it runs as several instances behind a load balancer against one shared database, with no single point of failure on the endpoints your router talks to. For the integration this means two things worth knowing.

- **Reshipping is safe under a load balancer.** Receipts are de-duplicated by id across the whole fleet, not just within one instance. If your router retries a batch and the retry lands on a different instance than the first try, the receipt is still stored exactly once. You do not need sticky sessions or instance affinity for ingest.
- **Policy is consistent across instances.** The ETag is derived from the policy content, not from which instance answered, so `If-None-Match` keeps working when your polls are spread across instances. A poll that hits instance A and the next that hits instance B agree on the ETag, and an unchanged policy still comes back 304.

This was verified with three collector instances behind nginx over one database: ingest deduped correctly across instances, and a sustained load through the balancer ran with no errors. The behaviour your router relies on, idempotent ingest and conditional polling, holds whether the collector is one instance or a fleet.

## Method and caveats

- k6 v2, three concurrent scenarios, about 41 seconds, up to 40 virtual users. Collector on .NET 10 in Release over PostgreSQL 16.
- Client and collector on the same host, so no network round trip is included. Add your own.
- The latency figures are one instance under a short burst, not a soak and not a capacity benchmark of a cluster over a day. The scale-out section above is a correctness check of running as a fleet, not a throughput number for one.
