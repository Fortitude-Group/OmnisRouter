# OmnisVigil integration performance

When you point OmnisRouter at an OmnisVigil collector, two endpoints carry the traffic between them: receipts go up, policy comes down. This note records what to expect from those two under load, so you can size a deployment and understand the cost of polling before you turn it on. It covers only the router-to-collector contract; the rest of OmnisVigil is not the router's concern.

The figures come from a k6 run against a single collector instance. Read them as a floor. The client and the collector shared one machine, so there is no network latency in these numbers, and it was one instance with no scale-out.

## Receipts up: `POST /ingest/v1/receipts`

The router batches content-free receipts and ships them here.

- A single collector instance sustained about **130 receipts a second** with single-receipt batches, at a p95 of **227ms** per request, with no failures.
- Your router batches many receipts per request, so the effective receipt rate in practice is higher than that.
- Reporting is fail-open. If the collector is slow or briefly unreachable, the router keeps routing and reships later, so ingest latency never blocks a request. Receipts are de-duplicated by their id, so reshipping after an outage is safe.

## Policy down: `GET /policy/v1/current`

The router polls here for the current caps, allowed models, and kill state.

- The response carries an ETag. Send it back as `If-None-Match`, and an unchanged policy comes back **304 Not Modified** with no body.
- In the test, **23,924 of 23,944 polls were 304**; only the first poll per client was a full 200. Poll p95 was **52ms**, including those occasional full responses.
- So polling once a minute across a large fleet costs the collector very little. Enforcement is local and fail-safe: the router keeps enforcing the last known policy if the collector is briefly unreachable, so a poll that fails never opens the gate.

## Method and caveats

- k6 v2, three concurrent scenarios, about 41 seconds, up to 40 virtual users. Collector on .NET 10 in Release over PostgreSQL 16.
- Client and collector on the same host, so no network round trip is included. Add your own.
- One instance, one database, no horizontal scale, and not a soak test. These say what a single collector does under a short burst, not what a cluster does over a day.
