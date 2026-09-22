# PLAYSPOT / COURTBOOK — PHASE 9.2: ADVANCED MATCHMAKING & GAME LOBBY REPORT

## 1. Executive Summary

Phase 9.2 delivers a production-grade, deterministic matchmaking and real-time game lobby infrastructure for the PlaySpot platform. It introduces multi-sport proficiency rating for athletes, an explainable 100-point compatibility recommendation engine, private games with access-code protection, multi-context optimistic concurrency capacity safeguards, interactive game lobbies powered by SignalR, and a deterministic snake-draft team balancing algorithm.

All existing platform capabilities—including the complete Phase 7/8 payment gateway, booking reservation engine, real-time user notification pipeline, and Phase 9.1 mobile authentication/refresh token rotation—remain 100% functional with zero regressions.

---

## 2. Key Architecture & Domain Model Enhancements

### 2.1 Sport-Specific Skill Model (`PlayerSportSkill`)
Rather than relying on a single global skill score across divergent sports (e.g. Football vs Padel), PlaySpot now models player competence per sport:
- **Entity**: `PlayerSportSkill`
- **Fields**: `UserId`, `SportType`, `SkillLevel` (Beginner, Intermediate, Advanced), `SkillScore` (numerical rating clamped between 500 and 3,000, default 1,000 for Beginner, 1,500 for Intermediate, 2,000 for Advanced), `MatchesPlayed`, and `UpdatedAt`.
- **Database Indexing**: Unique composite index on `(UserId, SportType)` ensures a single normalized skill rating per sport per user with cascade deletion on user removal.

### 2.2 Extended Matchmaking Preferences (`PlayerPreference`)
Expanded player configuration for fine-grained search and recommendation targeting:
- `PreferredSports`: Comma-separated or multi-sport list.
- `PreferredCities`: Geographical location preference list.
- `PreferredDays`: Compatible days of week (e.g., "Friday,Saturday").
- `PreferredTimeOfDay`: Time slot preference (Morning, Afternoon, Evening, Night).
- `PreferredGameType`: Match style (Casual, Competitive, Friendly).
- `MaxDistanceKm`: Travel radius allowance.
- `PreferredSkillLevel`: Desired competitor level.

### 2.3 Game Privacy & Concurrency Security (`Game`, `GameParticipant`)
- `Game.IsPrivate`: Boolean flag demarcating private matches from general public search.
- `Game.AccessCode`: 6-character cryptographic alphanumeric passcode (generated via `RandomNumberGenerator`) required to join private games.
- `Game.HasTeams`: Flag designating whether the match requires team allocation.
- `Game.ConcurrencyStamp`: Optimistic concurrency token mapped via `.IsConcurrencyToken()` preventing capacity race conditions.
- `GameParticipant.Team`: String identifier ("TeamA", "TeamB", or unassigned).
- `GameParticipant.IsReady`: Boolean player readiness indicator.
- `GameParticipant.ConcurrencyStamp`: Token protecting atomic state updates.
- Unique DB index on `GameParticipants(GameId, UserId)` eliminating duplicate participation.

---

## 3. Deterministic Matchmaking Engine (`IMatchmakingService`)

The matchmaking engine evaluates open games deterministically without reliance on external AI/LLM dependencies or opaque black-box scoring.

### 3.1 Mathematical Scoring Formula (0 to 100 Points)

$$\text{Total Match Score} = S_{\text{sport}} (40) + S_{\text{skill}} (25) + S_{\text{time}} (20) + S_{\text{loc}} (15)$$

1. **Sport Compatibility (Max 40 pts)**:
   - **40 pts**: Game sport explicitly listed in player's `PreferredSports`.
   - **30 pts**: Player has an active `PlayerSportSkill` profile in the sport.
   - **25 pts**: General profile match when no explicit preference list is configured.
   - **10 pts**: Alternative sports.

2. **Skill Compatibility (Max 25 pts)**:
   - Evaluates athlete's sport-specific rating (or profile skill):
     - **25 pts**: Exact skill match ($L_{\text{game}} = L_{\text{player}}$).
     - **20 pts**: Open/All-Levels games ($L_{\text{game}} = \text{AllLevels}$).
     - **12 pts**: Adjacent skill level ($|L_{\text{game}} - L_{\text{player}}| = 1$).
     - **0 pts**: Incompatible skill gap ($|L_{\text{game}} - L_{\text{player}}| \ge 2$).

3. **Time / Day Compatibility (Max 20 pts)**:
   - **Day Compatibility (10 pts)**: Game day of week in player's `PreferredDays` (5 pts if unrestricted).
   - **Time Slot Compatibility (10 pts)**: Game start time matches player's `PreferredTimeOfDay` category (Morning: 06:00–12:00, Afternoon: 12:00–17:00, Evening: 17:00–22:00, Night: 22:00–06:00; 5 pts if unrestricted).

4. **Location / City Match (Max 15 pts)**:
   - **15 pts**: Venue city matches player's `PreferredCities`.
   - **8 pts**: Flexible/unspecified city preferences.
   - **0 pts**: Different city.

### 3.2 Hard Eligibility Filters
- **Status Filter**: Must be active `GameStatus.Open`.
- **Timing Filter**: Date and time must be in the future.
- **Participation Filter**: Creator and existing participants are excluded.
- **Age Eligibility**: Strictly verifies `User.DateOfBirth` against `AgeGroup`, `MinAge`, and `MaxAge`. Ineligible players are filtered out automatically.
- **Privacy Filter**: Private games are excluded from public recommendations.

---

## 4. Real-Time Game Lobby Architecture (`GameLobbyHub`)

### 4.1 SignalR Hub Security
- **Route**: `/hubs/games`
- **Authentication**: JWT Bearer token passed via Authorization header or `?access_token=` query string for WebSocket transport.
- **Group Isolation**: Isolated real-time groups named `game:{gameId}`.
- **Membership Enforcement**: Callers attempting to join `JoinLobby(gameId)` are authorized against the database. For private games, unauthorized spectators are rejected with a SignalR exception.

### 4.2 Dispatched Real-Time Events
| Event Name | Payload | Trigger Condition |
| :--- | :--- | :--- |
| `PlayerJoined` | `GameParticipantDto` | A new player joins the match |
| `PlayerLeft` | `{ userId, userName }` | A player leaves the match |
| `GameFull` | `{ gameId }` | Last available slot is confirmed |
| `PlayerReady` | `{ userId, isReady, allPlayersReady }` | Player toggles ready status |
| `TeamUpdated` | `BalanceTeamsResponse` | Organizer re-balances or assigns teams |
| `GameCancelled` | `{ gameId }` | Organizer/Admin cancels the match |

---

## 5. Team Formation & Deterministic Snake-Draft Balancing

### 5.1 Snake-Draft Balancing Algorithm
To ensure fair and competitive matches, organizers can trigger automated team balancing:
1. Retrieve all registered participants and their sport skill ratings.
2. Sort players descending by `SkillScore`, breaking ties by `JoinedAt` ascending.
3. Apply a snake draft distribution across teams:
   - Rank 0 $\rightarrow$ Team A
   - Rank 1 $\rightarrow$ Team B
   - Rank 2 $\rightarrow$ Team B
   - Rank 3 $\rightarrow$ Team A
   - (Pattern: $A, B, B, A, A, B, B, A \dots$)
4. Compute total aggregate skill score for each team and total skill difference.
5. Persist participant assignments in the database and broadcast `TeamUpdated` to the lobby.

---

## 6. Web UI & Client Experience

1. **Community Games Directory (`/Games`)**:
   - **Personalized Recommendations Carousel**: Shows top 3 matches ranked by compatibility percentage with explainable breakdown tooltips.
   - **Private Match Joining**: Inline access-code entry for private games.
   - **Lobby Navigation**: Direct button linking confirmed participants into their interactive lobby.
2. **Interactive Match Lobby (`/Games/Lobby?gameId={id}`)**:
   - Live roster and team breakdown (Team A vs Team B).
   - Real-time ready status toggle (`Ready` / `Not Ready`).
   - "All Players Ready" banner indication when minimum players are met and everyone is ready.
   - Organizer controls: One-click "Balance Teams (Snake Draft)".
   - Real-time client-side SignalR connection automatically updating the view on lobby changes.

---

## 7. Automated Test Suite & Verification Results

A comprehensive suite was developed in `Phase92MatchmakingTests.cs` covering all matchmaking and lobby workflows:
- `PlayerSportSkill_Upsert_CreatesAndUpdatesSkillRatingWithAccurateDefaults`: PASSED
- `PlayerPreference_Updates_AndRetrievesPreferencesAccurately`: PASSED
- `Matchmaking_CalculatesDeterministicRecommendationScore_WithBreakdown`: PASSED (100.0/100 perfect match verified)
- `Matchmaking_RanksCandidateGames_ByHighestCompatibilityScore`: PASSED
- `Matchmaking_ExcludesIneligibleAgeAndAlreadyJoinedGames`: PASSED
- `Game_Private_RequiresValidAccessCode_ToJoin`: PASSED (Forbidden on missing/wrong code, Success on correct code)
- `Game_Private_ExcludedFromDefaultSearch`: PASSED
- `Game_DuplicateJoin_ReturnsConflict`: PASSED
- `GameLobby_SetReady_UpdatesPlayerStateAndAllPlayersReadyFlag`: PASSED
- `GameLobby_BalanceTeams_UsesSnakeDraft_AndMinimizesSkillGap`: PASSED (0 score delta achieved)
- `GameLobby_AssignTeam_RestrictedToOrganizer`: PASSED
- `GameLobby_LeaveGame_RestoresStatusToOpen_WhenWasFull`: PASSED
- `Game_ConcurrentJoin_ProtectsCapacityUnderRaceConditions`: PASSED (Capacity never exceeded under race conditions)

### Final Test Summary:
- **Total Tests Passed**: 218 / 218 (100% passing)
- **Payment & Concurrency Regressions**: 83 / 83 passing
- **Mobile Auth & Session Regressions**: 17 / 17 passing
- **Build Quality**: `dotnet build --warnaserror` succeeds with **0 warnings, 0 errors**.
