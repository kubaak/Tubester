I want to add a subscription history archive table to Tubester.

Context:
- The current `Subscriptions` table represents the current subscription state for a user.
- I want to keep that behavior.
- Add a new `SubscriptionHistory` entity/table that stores previous subscription periods when a subscription period is renewed/expired.
- Do not add a surrogate `Guid Id`.
- Use a composite primary key.

Requirements:

1. Add a new domain/entity class `SubscriptionHistory` with these properties:
    - string UserId
    - Guid PlanId
    - SubscriptionStatus Status
    - DateTimeOffset PeriodStartUtc
    - DateTimeOffset PeriodEndUtc
    - DateTimeOffset CreatedAtUtc

2. Add `DbSet<SubscriptionHistory>` to `TubesterDb`.

3. Configure EF Core mapping:
    - Table name: `SubscriptionHistories`
    - Composite primary key:
        - UserId
        - PlanId
        - PeriodStartUtc
        - PeriodEndUtc
    - Configure `UserId` as required.
    - Configure relationship to `Plan` using `PlanId`.
    - Use delete behavior `Restrict` or `NoAction` for the plan relationship.
    - Store `SubscriptionStatus` consistently with the existing `Subscription.Status` mapping. If existing subscriptions store it as string, use string here too. If they store it as int, use int here too.

4. Add a migration for the new table.

5. Do not change the current `Subscriptions` behavior yet.
    - Do not change renewal logic in this task.
    - Do not modify credit spending logic in this task.
    - Only add the entity/table/mapping/migration.

6. Keep the implementation consistent with the existing project conventions:
    - nullable reference types
    - sealed entity classes if that is the existing style
    - existing namespace/folder conventions
    - existing EF Core configuration style

Expected result:
- The project builds.
- A migration exists that creates `SubscriptionHistories`.
- The table has no `Id` column.
- The composite primary key prevents duplicate archived rows for the same user, plan, and period.