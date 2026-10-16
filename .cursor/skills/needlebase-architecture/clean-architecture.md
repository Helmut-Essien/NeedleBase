# Clean Architecture Rules

## Layer responsibilities

### Domain (`NeedleBase.Domain`)

- Entities: Business, Customer, MeasurementProfile, Order, OrderLine, Payment, StaffMembership, Invitation, Subscription snapshot fields as needed
- Value objects / enums: OrderStatus, PaymentMethod, Role, SyncState (as domain concepts)
- Domain invariants (e.g. payment amount > 0; order cannot go Collected without Ready if that is a rule)
- **No** EF attributes required if Infrastructure configures mapping (prefer fluent config in Infrastructure). Avoid leaking persistence into Domain when practical.

### Application (`NeedleBase.Application`)

- Use cases / application services: `ICreateOrder`, `IRecordPayment`, `ISyncPush`, etc.
- Interfaces for infrastructure: `IBusinessDbContext` (or repositories), `IEmailSender`, `IClock`, `ICurrentUser`
- DTOs / API contracts shared with Mobile and Web (records)
- Authorization policies expressed as requirements (Owner/Manager/Tailor) evaluated with `BusinessId` + role
- Mapping: Domain ↔ DTO (manual or mapper — keep consistent)

### Infrastructure (`NeedleBase.Infrastructure`)

- `ApplicationDbContext` (SQL Server), Identity, migrations
- Token service, email, file/blob for style images if needed
- Sync push/pull application on server (`DeletedItem`, distributed lock per user/business)
- Implements Application interfaces

### Api (`NeedleBase.Api`)

- Thin controllers or minimal APIs → Application use cases
- JWT auth, `X-Auth-Token` → Authorization mapping (AssetTag pattern)
- Health: `/health/live`, `/health/ready`, mobile ping endpoint
- Composition root: register Infrastructure

### Web (`NeedleBase.Web`)

- UI only; named HttpClients to Api
- Cookie/session for browser; never EF

### Mobile (`NeedleBase.Mobile`) + Mobile.Data

- **MVVM** presentation (Views/ViewModels)
- ViewModels call application-facing mobile services / use-case facades that read/write **local** DB and enqueue sync
- `LocalDbContext` in Mobile.Data; suppress sync queue on pull

## Use-case flow examples

**Online create order (Api/Web):**
Controller/Page → `ICreateOrder.Handle` → domain validate → persist → DTO

**Offline create order (Mobile):**
ViewModel → `IOrderLocalService.CreateAsync` → SQLite save → SyncQueue CREATE → toast “Saved on this device” → later SyncService push

## Anti-patterns

- Domain project referencing EF Core or MAUI
- Web PageModel with `DbContext`
- Mobile ViewModel calling SQL Server directly
- Duplicating DTOs in Api and Mobile
- Business logic only in controllers or only in ViewModels
- Skipping `BusinessId` filters “for convenience”

## Testing guidance

| Layer | Test focus |
|-------|------------|
| Domain | Invariants, status transitions |
| Application | Use cases with mocked interfaces |
| Api | Auth + contract smoke tests |
| Mobile ViewModels | Commands, busy flags, offline paths with substitutes |
