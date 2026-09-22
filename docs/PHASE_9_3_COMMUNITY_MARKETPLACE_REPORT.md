# PLAYSPOT / COURTBOOK — PHASE 9.3: COMMUNITY & MARKETPLACE LAYER REPORT

## 1. Executive Summary

Phase 9.3 delivers a complete **Community & Marketplace Layer** for the PlaySpot platform. It introduces peer-to-peer game invitations, a lightweight player social connection graph, an enforceable user blocking mechanism, historical match analytics, and an explainable sports reputation system.

All newly introduced features integrate seamlessly with existing platform systems—including the Clean Architecture domain, EF Core database layer, ASP.NET Identity, JWT bearer auth, SignalR real-time hubs, and the Payment gateway—preserving **100% backward compatibility** and **zero regressions**.

---

## 2. Baseline & Verification Summary

- **Starting Baseline Commit**: `a3a62b6` (`security(phase-9.2): harden matchmaking, game lobby, and privacy guards`)
- **Automated Tests**: **241 / 241 passed** (100% success rate across all 34 test suites, including 19 new Phase 9.3 community tests).
- **Payment & Concurrency Invariants**: **46 / 46 payment tests** (and 83 / 83 total booking/payment concurrency tests) green.
- **Mobile Auth Invariants**: **14 / 14 mobile authentication and token family tests** green.
- **Compiler Cleanliness**: `dotnet build --warnaserror` succeeds with **0 warnings and 0 errors**.
- **Database Migration**: `20260922170959_AddCommunityAndMarketplaceLayer` created cleanly and verified.

---

## 3. Game Invitations Subsystem

### 3.1 Architecture & Domain Model
The `GameInvitation` entity manages match invitations sent between players:
- **Entity**: `GameInvitation`
- **Fields**: `Id`, `GameId`, `InviterId`, `InviteeId`, `Status` (`InvitationStatus`), `Message`, `ExpiresAt`, `CreatedAt`, `RespondedAt`, `ConcurrencyStamp`.
- **Database Indexing**:
  - `(GameId, InviteeId, Status)`: Prevents duplicate active invitations and accelerates match-level invitation lookups.
  - `(InviteeId, Status)`: Optimizes user inbox queries.
  - `(InviterId, Status)`: Optimizes sent invitation queries.

### 3.2 Invitation Lifecycle State Machine
```
       [Created by Creator / Participant]
                       ↓
                   [Pending]
                  /    |    \
     (Invitee)   /     |     \  (Invitee)
                ↓      |      ↓
           [Accepted]  |   [Declined]
                       |
                       +-----> [Expired]    (Auto-evaluated at read/accept time)
                       |
                       +-----> [Cancelled]  (Inviter/Creator cancels or Block occurs)
```

### 3.3 Strict Authorization & Joining Integrity
1. **Authorization**: Only the game creator or confirmed match participants can send invitations. Strangers or unauthenticated users receive `403 Forbidden`.
2. **Self-Invitation Prevention**: Users cannot invite themselves (`400 BadRequest`).
3. **Block Enforcement**: Invitations cannot be created between users with a `Blocked` relationship.
4. **Duplicate Prevention**: A second active pending invitation for the same player and game is rejected with `409 Conflict`.
5. **Eligibility Verification**: Target player must meet age requirements (`DateOfBirth` vs `AgeGroup`/`MinAge`/`MaxAge`) and match capacity cannot be exceeded.
6. **Critical Acceptance Pipeline**: `AcceptInvitationAsync` reuses `GameService.JoinGameAsync` under the hood. It utilizes optimistic concurrency tokens (`Game.ConcurrencyStamp`), capacity checking, and duplicate join detection, ensuring invitation acceptance never bypasses platform security or capacity limits.

---

## 4. Player Connections & Social Graph

### 4.1 Architecture & Domain Model
- **Entity**: `PlayerConnection`
- **Fields**: `Id`, `RequesterId`, `AddresseeId`, `Status` (`ConnectionStatus`: `Pending`, `Accepted`, `Declined`, `Blocked`), `CreatedAt`, `UpdatedAt`.
- **Uniqueness Constraint**: Unique index on `(RequesterId, AddresseeId)` prevents redundant connection requests in the same direction.

### 4.2 Connection Lifecycle & Mutual Request Auto-Acceptance
- **Request**: User A sends request to User B $\rightarrow$ status `Pending`, notification dispatched to User B.
- **Mutual Request Detection**: If User B sends a connection request to User A while User A has a pending request to User B, the system automatically detects the mutual intent and transitions the connection to `Accepted` immediately.
- **Acceptance / Decline**: Addressee can accept or decline. Requester is notified upon acceptance.
- **Removal**: Either party can remove a confirmed connection at any time.

---

## 5. Block / Unblock System

### 5.1 Enforcement Boundaries
When User A blocks User B:
1. A `PlayerConnection` record with `Status = ConnectionStatus.Blocked` is persisted.
2. Any pending game invitations between User A and User B are automatically cancelled.
3. User B cannot send connection requests to User A (`403 Forbidden`).
4. User B cannot invite User A to any games (`403 Forbidden`).
5. Matchmaking recommendation queries automatically filter out games organized by blocked users.
6. User A can unblock User B, deleting the block record and restoring neutral interaction capabilities.

---

## 6. Game History Subsystem

### 6.1 Database Filtering & Performance
- **Endpoint**: `GET /api/community/players/{id}/history`
- **Semantics**: Retrieves past matches (`Game.Date < today` or `(Game.Date == today && Game.StartTime <= nowTime)` or `Game.Status == Completed`) where the user was a confirmed participant (`GameParticipants`).
- **Database-Side Filtering**: Filters by `SportType`, `Date Range`, and `Status` using SQL `WHERE` clauses and pagination (`Skip(request.Skip).Take(request.PageSize)`).
- **Privacy & User Isolation**: When third parties view a player's history, private matches where the viewer was not a participant are omitted from the projection.

---

## 7. Explainable Player Reputation Engine

### 7.1 Mathematical Scoring Formula
Rather than arbitrary or black-box ratings, player reputation is calculated deterministically from documented system activity:

$$\text{Attendance Rate} = \begin{cases} 100.0\% & \text{if } N_{\text{joined}} = 0 \\ \min\left(100.0, 100.0 \times \frac{N_{\text{completed}}}{N_{\text{joined}}}\right) & \text{otherwise} \end{cases}$$

$$\text{Cancellation Penalty} = \begin{cases} 0.0 & \text{if } N_{\text{organized}} = 0 \\ \min\left(30.0, 30.0 \times \frac{N_{\text{cancelled}}}{N_{\text{organized}}}\right) & \text{otherwise} \end{cases}$$

$$\text{Reliability Score} = \text{clamp}_{[0, 100]}(\text{Attendance Rate} - \text{Cancellation Penalty})$$

### 7.2 Transparency & Privacy
- **Tiers**: $\ge 90\% \rightarrow$ "Elite / Very Reliable", $\ge 75\% \rightarrow$ "Reliable", $\ge 50\% \rightarrow$ "Fair", $< 50\% \rightarrow$ "Needs Improvement" (New users default to "Building History").
- **Privacy Hardening**: Public profiles expose only public info (Name, Bio, Sports, Skills, Reputation Score, and Connection status). Emails, phone numbers, IP addresses, and refresh tokens are strictly protected and never exposed.

---

## 8. Community Notifications & SignalR Integration

### 8.1 Real-Time Dispatched Events
Integrated with `NotificationService` and `SignalRNotificationSender` via `NotificationHub`:
| Event | Trigger | Recipient | Action Link |
| :--- | :--- | :--- | :--- |
| `GameInvite` | Player receives game invitation | Invitee | `/Community/Invitations` |
| `GameInviteAccepted` | Invitee accepts game invitation | Inviter | `/Games/Lobby?gameId={id}` |
| `GameInviteDeclined` | Invitee declines game invitation | Inviter | Notification Center |
| `ConnectionRequest` | User receives connection request | Addressee | `/Community?ActiveTab=pending` |
| `ConnectionAccepted` | Connection request accepted | Requester | `/Profile/Public?userId={id}` |

---

## 9. API Endpoints Reference

### 9.1 Invitations (`/api/invitations` & `/api/games/{gameId}/invitations`)
- `POST /api/games/{gameId}/invitations`: Create game invitation (Rate-limited, Authorize).
- `GET /api/invitations/received`: Paginated list of received invitations (Authorize).
- `GET /api/invitations/sent`: Paginated list of sent invitations (Authorize).
- `POST /api/invitations/{id}/accept`: Concurrency-protected acceptance (Authorize).
- `POST /api/invitations/{id}/decline`: Decline invitation (Authorize).
- `DELETE /api/invitations/{id}`: Cancel invitation (Authorize).

### 9.2 Connections (`/api/connections`)
- `GET /api/connections`: Paginated list of connections (Authorize).
- `POST /api/connections`: Send connection request (Rate-limited, Authorize).
- `POST /api/connections/{id}/accept`: Accept request (Authorize).
- `POST /api/connections/{id}/decline`: Decline request (Authorize).
- `DELETE /api/connections/{id}`: Remove connection (Authorize).
- `POST /api/connections/block`: Block a user (Rate-limited, Authorize).
- `POST /api/connections/unblock`: Unblock a user (Authorize).
- `GET /api/connections/blocked`: List blocked users (Authorize).

### 9.3 Community & Players (`/api/community/players`)
- `GET /api/community/players/search`: Search community players by sport/city (Authorize).
- `GET /api/community/players/{id}/profile`: Public player profile (AllowAnonymous).
- `GET /api/community/players/{id}/reputation`: Explainable reputation metrics (AllowAnonymous).
- `GET /api/community/players/{id}/history`: Paginated match history with filters (Authorize).

---

## 10. Rate Limiting & Anti-Spam Safeguards

- Implemented `"community"` rate limiter policy partitioned by authenticated user ID (or remote IP):
  - **Permit Limit**: 30 requests per minute.
  - **Rejection Status**: `429 Too Many Requests`.
  - Protects invitation creation, connection requests, and block mutations against automated spamming.

---

## 11. Concurrency Verification & Race Mitigation

- **Game Slot Racing**: Tested concurrent invitation acceptances when exactly 1 spot remains (`Invitation_ConcurrentAcceptance_ProtectsGameCapacityUnderRace`). Handled via `Game.ConcurrencyStamp` and optimistic concurrency; exactly 1 racer succeeds, excess racer receives 409 Conflict, and game capacity is never breached.
- **IDOR Protection**: Verified that unauthorized third parties cannot accept, decline, or cancel invitations or connections belonging to other users.

---

## 12. Automated Test Results

```
Test run for CourtBook.Tests.dll (.NETCoreApp,Version=v10.0)
Passed!  - Failed: 0, Passed: 241, Skipped: 0, Total: 241, Duration: 5 s
```

- **Total Tests Executed**: 241
- **Passed**: 241 (100%)
- **Failed**: 0
- **Build Quality**: `0 Warning(s)`, `0 Error(s)` with `dotnet build --warnaserror`.

---

## 13. Known Limitations & Future Improvements

1. **Paymob Sandbox Status**: Real Paymob Sandbox validation remains externally blocked until user credentials are provided in staging/production environment.
2. **Direct In-App Chat**: Direct player-to-player messaging is not part of Phase 9.3 and can be added in future community extensions.
3. **Automated No-Show Tracking**: Check-in QR code scanning at venues can be introduced in future phases to augment attendance verification.
