# Scaffold defects

1. **Unsafe cache** — `SimpleObjectCache` stored state in a plain `Dictionary`, which corrupts under concurrent reads and writes, so it is now backed by `IMemoryCache` with a locked key index for safe snapshots.
2. **Non-atomic reservation** — `TryReserveAsync` checked stock, decremented it, and created the reservation as separate steps, allowing overselling and lost updates, so the complete transition now runs under per-key semaphores with the reservation locked before the product.
3. **Wrong HTTP response** — `ReservationsController` returned `200 OK` when stock was insufficient, which misrepresented a business-rule failure, so it now returns `409 Conflict`.
4. **Incorrect expiry clock** — `IsExpired` compared a UTC `ExpiresAt` with local `DateTime.Now`, making expiry timezone-dependent, so it now compares against `DateTime.UtcNow`.
5. **Broken background-service lifecycle** — `ReservationExpirySweeper` started an untracked fire-and-forget loop that blocked with `Thread.Sleep`, synchronously waited on async work, and ignored shutdown cancellation, so it is now a `BackgroundService` using an awaited `PeriodicTimer` that observes the stopping token.

## Additional hardening

The original five scaffold defects are listed above; the items below are follow-up requirements rather than additional scaffold defects.

- **Confirmed reservations** — Releasing a `Confirmed` reservation previously flipped it back to `Released` and returned its stock, so `ReleaseAsync` now returns `409 Conflict` without changing the status or stock.
- **Negative inventory** — Direct service calls could create a product with negative stock or reserve a non-positive quantity, so `CreateProductAsync` now rejects negative stock and `TryReserveAsync` returns `InvalidQuantity`, which the controller maps to `400 Bad Request`.

## Scope

The per-key semaphores serialize transitions on the same entity for a single API instance; multiple instances would require a shared transactional datastore instead of process-local coordination.
