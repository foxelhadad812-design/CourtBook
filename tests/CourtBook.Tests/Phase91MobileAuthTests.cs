using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CourtBook.API.Controllers;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Persistence;
using CourtBook.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CourtBook.Tests;

public class Phase91MobileAuthTests
{
    private class TestTokenService : ITokenService
    {
        private readonly int _expiryMinutes;

        public TestTokenService(int expiryMinutes = 60)
        {
            _expiryMinutes = expiryMinutes;
        }

        public (string Token, DateTime ExpiresAt) GenerateAccessToken(User user)
        {
            var expiresAt = DateTime.UtcNow.AddMinutes(_expiryMinutes);
            return ($"fake_jwt_token_{user.Id}_{Guid.NewGuid():N}", expiresAt);
        }

        public string GenerateToken(User user) => GenerateAccessToken(user).Token;
    }

    private static IConfiguration CreateConfiguration(int? expiryMinutes = null, int? expiryDays = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["JwtSettings:Key"] = "Super_Secret_Key_For_Phase_9_1_Testing_Only_32_Chars!",
            ["JwtSettings:Issuer"] = "PlaySpotTest",
            ["JwtSettings:Audience"] = "PlaySpotMobile",
        };

        if (expiryMinutes.HasValue)
            dict["JwtSettings:ExpiryInMinutes"] = expiryMinutes.Value.ToString();

        if (expiryDays.HasValue)
            dict["JwtSettings:ExpiryInDays"] = expiryDays.Value.ToString();

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [Fact]
    public async Task RegisterAsync_IssuesBothAccessTokenAndRefreshTokenSession()
    {
        // Arrange
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TestTokenService();
        var authService = new AuthService(db, tokenService);

        var request = new RegisterRequest
        {
            Name = "Karim Mobile",
            Email = "karim.mobile@test.com",
            Password = "Password123!",
            Phone = "+201012345678",
            Role = "Client",
            AcceptTerms = true,
            DeviceId = "device_ios_01",
            DeviceName = "Karim's iPhone 15 Pro",
            Platform = "iOS",
            AppVersion = "2.1.0"
        };

        // Act
        var response = await authService.RegisterAsync(request, "192.168.1.50");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Token);
        Assert.Equal(response.Token, response.AccessToken);
        Assert.True(response.AccessTokenExpiresAt > DateTime.UtcNow);
        Assert.NotEmpty(response.RefreshToken);
        Assert.True(response.RefreshTokenExpiresAt > DateTime.UtcNow.AddDays(29));
        Assert.NotEqual(Guid.Empty, response.UserId);
        Assert.Equal("karim.mobile@test.com", response.Email);
        Assert.Equal("Client", response.Role);
        Assert.Equal("Bearer", response.TokenType);

        // Verify entity in DB: raw token must NOT be stored in DB, only SHA-256 hash
        var storedTokens = await db.RefreshTokens.Where(r => r.UserId == response.UserId).ToListAsync();
        Assert.Single(storedTokens);
        var entity = storedTokens[0];
        Assert.NotEqual(response.RefreshToken, entity.TokenHash);
        Assert.Equal(AuthService.HashToken(response.RefreshToken), entity.TokenHash);
        Assert.Equal("device_ios_01", entity.DeviceId);
        Assert.Equal("Karim's iPhone 15 Pro", entity.DeviceName);
        Assert.Equal("iOS", entity.Platform);
        Assert.Equal("2.1.0", entity.AppVersion);
        Assert.Equal("192.168.1.50", entity.CreatedByIp);
        Assert.True(entity.IsActive);
        Assert.False(entity.IsRevoked);
        Assert.False(entity.IsExpired);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsNewRefreshTokenSession()
    {
        // Arrange
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TestTokenService();
        var authService = new AuthService(db, tokenService);

        var registerReq = new RegisterRequest
        {
            Name = "Nour User",
            Email = "nour@test.com",
            Password = "Password123!",
            Phone = "+201099887766",
            AcceptTerms = true
        };
        await authService.RegisterAsync(registerReq);

        var loginReq = new LoginRequest
        {
            Email = "nour@test.com",
            Password = "Password123!",
            DeviceId = "pixel_8",
            DeviceName = "Nour's Pixel 8",
            Platform = "Android",
            AppVersion = "1.0.4"
        };

        // Act
        var loginResponse = await authService.LoginAsync(loginReq, "10.0.0.12");

        // Assert
        Assert.NotNull(loginResponse);
        Assert.NotEmpty(loginResponse.AccessToken);
        Assert.NotEmpty(loginResponse.RefreshToken);

        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == AuthService.HashToken(loginResponse.RefreshToken));
        Assert.NotNull(stored);
        Assert.Equal("pixel_8", stored.DeviceId);
        Assert.Equal("Android", stored.Platform);
        Assert.Equal("10.0.0.12", stored.CreatedByIp);
    }

    [Fact]
    public async Task RefreshTokenAsync_RotatesToken_RevokesOldAndCreatesNewInSameFamily()
    {
        // Arrange
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TestTokenService();
        var authService = new AuthService(db, tokenService);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Tarek Player",
            Email = "tarek@test.com",
            Password = "Password123!",
            Phone = "+201055554444",
            AcceptTerms = true,
            DeviceId = "dev_1",
            Platform = "iOS"
        });

        var token1Raw = reg.RefreshToken;
        var token1Hash = AuthService.HashToken(token1Raw);

        var refreshReq = new RefreshTokenRequest
        {
            RefreshToken = token1Raw,
            DeviceId = "dev_1",
            Platform = "iOS",
            AppVersion = "2.0.0"
        };

        // Act - Rotate token
        var rotatedResponse = await authService.RefreshTokenAsync(refreshReq, "192.168.1.100");

        // Assert
        Assert.NotNull(rotatedResponse);
        Assert.NotEmpty(rotatedResponse.AccessToken);
        Assert.NotEmpty(rotatedResponse.RefreshToken);
        Assert.NotEqual(token1Raw, rotatedResponse.RefreshToken);

        // Check Token 1 in DB: marked revoked as 'Rotated' and links to Token 2
        var token1Entity = await db.RefreshTokens.FirstAsync(r => r.TokenHash == token1Hash);
        Assert.True(token1Entity.IsRevoked);
        Assert.Equal("Rotated", token1Entity.ReasonRevoked);
        Assert.NotNull(token1Entity.ReplacedByTokenId);

        // Check Token 2 in DB: active and shares FamilyId
        var token2Hash = AuthService.HashToken(rotatedResponse.RefreshToken);
        var token2Entity = await db.RefreshTokens.FirstAsync(r => r.TokenHash == token2Hash);
        Assert.True(token2Entity.IsActive);
        Assert.Equal(token1Entity.FamilyId, token2Entity.FamilyId);
        Assert.Equal(token1Entity.ReplacedByTokenId, token2Entity.Id);
        Assert.Equal("2.0.0", token2Entity.AppVersion);
    }

    [Fact]
    public async Task RefreshTokenAsync_ReusingRevokedToken_TriggersFamilyRevocation()
    {
        // Arrange
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TestTokenService();
        var authService = new AuthService(db, tokenService);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Victim User",
            Email = "victim@test.com",
            Password = "Password123!",
            Phone = "+201000001111",
            AcceptTerms = true
        });

        var token1Raw = reg.RefreshToken;

        // Normal rotation 1 -> Token 2 issued
        var rot1 = await authService.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = token1Raw });
        Assert.NotNull(rot1);
        var token2Raw = rot1.RefreshToken;

        // Verify Token 2 is currently active
        var token2Hash = AuthService.HashToken(token2Raw);
        var token2Before = await db.RefreshTokens.FirstAsync(r => r.TokenHash == token2Hash);
        Assert.True(token2Before.IsActive);

        // Act: Attacker attempts to reuse Token 1 (which was already rotated)
        var reuseEx = await Assert.ThrowsAsync<SecurityTokenException>(async () =>
        {
            await authService.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = token1Raw });
        });

        // Assert reuse exception
        Assert.Contains("reuse detected", reuseEx.Message, StringComparison.OrdinalIgnoreCase);

        // Zero-Trust Verification: Token 2 (and any other tokens in the family) must now be revoked!
        var token2After = await db.RefreshTokens.FirstAsync(r => r.TokenHash == token2Hash);
        Assert.True(token2After.IsRevoked);
        Assert.Equal("Revoked due to detected token reuse attack", token2After.ReasonRevoked);

        // Subsequent attempt with Token 2 must also fail
        await Assert.ThrowsAsync<SecurityTokenException>(async () =>
        {
            await authService.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = token2Raw });
        });
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredToken_ThrowsSecurityTokenExpiredException()
    {
        // Arrange
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TestTokenService();
        var authService = new AuthService(db, tokenService);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Expired Test",
            Email = "expired@test.com",
            Password = "Password123!",
            Phone = "+201099990000",
            AcceptTerms = true
        });

        // Force token to be expired in DB
        var tokenHash = AuthService.HashToken(reg.RefreshToken);
        var entity = await db.RefreshTokens.FirstAsync(r => r.TokenHash == tokenHash);
        entity.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<SecurityTokenExpiredException>(async () =>
        {
            await authService.RefreshTokenAsync(new RefreshTokenRequest { RefreshToken = reg.RefreshToken });
        });
    }

    [Fact]
    public async Task RevokeTokenAsync_LogsOutSingleSession()
    {
        // Arrange
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TestTokenService();
        var authService = new AuthService(db, tokenService);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Logout Test",
            Email = "logout@test.com",
            Password = "Password123!",
            Phone = "+201011112222",
            AcceptTerms = true
        });

        // Act
        var success = await authService.RevokeTokenAsync(reg.RefreshToken, "127.0.0.1", "User logged out");

        // Assert
        Assert.True(success);

        var tokenHash = AuthService.HashToken(reg.RefreshToken);
        var entity = await db.RefreshTokens.FirstAsync(r => r.TokenHash == tokenHash);
        Assert.True(entity.IsRevoked);
        Assert.Equal("User logged out", entity.ReasonRevoked);
    }

    [Fact]
    public async Task RevokeAllUserTokensAsync_TerminatesAllActiveSessionsAcrossDevices()
    {
        // Arrange
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TestTokenService();
        var authService = new AuthService(db, tokenService);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Multi Device",
            Email = "multi@test.com",
            Password = "Password123!",
            Phone = "+201033334444",
            AcceptTerms = true,
            DeviceId = "device_1"
        });

        // Second login from device 2
        var login2 = await authService.LoginAsync(new LoginRequest
        {
            Email = "multi@test.com",
            Password = "Password123!",
            DeviceId = "device_2"
        });
        Assert.NotNull(login2);

        // Verify 2 active tokens
        var activeCountBefore = await db.RefreshTokens
            .CountAsync(r => r.UserId == reg.UserId && r.RevokedAt == null);
        Assert.Equal(2, activeCountBefore);

        // Act: Logout all devices
        var terminatedCount = await authService.RevokeAllUserTokensAsync(reg.UserId);

        // Assert
        Assert.Equal(2, terminatedCount);

        var activeCountAfter = await db.RefreshTokens
            .CountAsync(r => r.UserId == reg.UserId && r.RevokedAt == null);
        Assert.Equal(0, activeCountAfter);
    }

    [Fact]
    public async Task GetUserSessionsAsync_And_RevokeSessionAsync_ManageIndividualDevices()
    {
        // Arrange
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TestTokenService();
        var authService = new AuthService(db, tokenService);

        var reg = await authService.RegisterAsync(new RegisterRequest
        {
            Name = "Sessions User",
            Email = "sessions@test.com",
            Password = "Password123!",
            Phone = "+201077778888",
            AcceptTerms = true,
            DeviceId = "iphone_15",
            DeviceName = "Personal iPhone",
            Platform = "iOS"
        });

        var login2 = await authService.LoginAsync(new LoginRequest
        {
            Email = "sessions@test.com",
            Password = "Password123!",
            DeviceId = "ipad_pro",
            DeviceName = "Work iPad",
            Platform = "iPadOS"
        });
        Assert.NotNull(login2);

        // Act: List sessions
        var sessions = await authService.GetUserSessionsAsync(reg.UserId, reg.RefreshToken);

        // Assert
        Assert.Equal(2, sessions.Count);
        var currentSession = sessions.First(s => s.IsCurrentSession);
        Assert.Equal("iphone_15", currentSession.DeviceId);
        Assert.True(currentSession.IsCurrentSession);

        var otherSession = sessions.First(s => !s.IsCurrentSession);
        Assert.Equal("ipad_pro", otherSession.DeviceId);

        // Act: Revoke the other session (iPad)
        var revokeSuccess = await authService.RevokeSessionAsync(reg.UserId, otherSession.SessionId);
        Assert.True(revokeSuccess);

        // Assert: iPad session is now revoked, only iPhone session remains active
        var sessionsAfter = await authService.GetUserSessionsAsync(reg.UserId);
        Assert.Single(sessionsAfter);
        Assert.Equal("iphone_15", sessionsAfter[0].DeviceId);
    }

    [Fact]
    public void TokenService_WithExpiryInMinutes_GeneratesProperJwtWithShortLifetime()
    {
        // Arrange
        var config = CreateConfiguration(expiryMinutes: 15);
        var tokenService = new TokenService(config);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "mobile.jwt@test.com",
            Name = "Mobile JWT User",
            Role = Role.Client
        };

        // Act
        var (tokenString, expiresAt) = tokenService.GenerateAccessToken(user);

        // Assert
        Assert.NotEmpty(tokenString);
        Assert.True(expiresAt > DateTime.UtcNow.AddMinutes(14));
        Assert.True(expiresAt <= DateTime.UtcNow.AddMinutes(16));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokenString);

        Assert.Equal("PlaySpotTest", jwt.Issuer);
        Assert.Contains("PlaySpotMobile", jwt.Audiences);
        Assert.Equal(user.Id.ToString(), jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.Email, jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(user.Name, jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Name).Value);
        Assert.Equal("Client", jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti));
    }

    [Fact]
    public async Task AuthController_FullEndToEndFlow()
    {
        // Arrange
        using var db = TestDbContextFactory.Create(Guid.NewGuid().ToString());
        var tokenService = new TestTokenService();
        var authService = new AuthService(db, tokenService);
        var controller = new AuthController(authService);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // 1. Register
        var regAction = await controller.Register(new RegisterRequest
        {
            Name = "Controller Flow",
            Email = "controller.flow@test.com",
            Password = "Password123!",
            Phone = "+201012345678",
            AcceptTerms = true,
            DeviceId = "test_device"
        });

        var regOk = Assert.IsType<OkObjectResult>(regAction);
        var regAuth = Assert.IsType<AuthResponse>(regOk.Value);
        Assert.NotEmpty(regAuth.AccessToken);
        Assert.NotEmpty(regAuth.RefreshToken);

        // 2. Refresh
        var refreshAction = await controller.Refresh(new RefreshTokenRequest
        {
            RefreshToken = regAuth.RefreshToken
        });

        var refreshOk = Assert.IsType<OkObjectResult>(refreshAction);
        var refreshAuth = Assert.IsType<AuthResponse>(refreshOk.Value);
        Assert.NotEmpty(refreshAuth.AccessToken);
        Assert.NotEqual(regAuth.RefreshToken, refreshAuth.RefreshToken);

        // 3. Compromised replay of old token -> 401 Unauthorized
        var replayAction = await controller.Refresh(new RefreshTokenRequest
        {
            RefreshToken = regAuth.RefreshToken
        });
        var replayUnauth = Assert.IsType<UnauthorizedObjectResult>(replayAction);
        Assert.NotNull(replayUnauth.Value);

        // 4. Setup ClaimsPrincipal on Controller for authenticated actions
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, regAuth.UserId.ToString()),
            new Claim(ClaimTypes.Role, "Client")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        httpContext.User = new ClaimsPrincipal(identity);

        // 5. Logout-all
        var logoutAllAction = await controller.LogoutAll();
        var logoutAllOk = Assert.IsType<OkObjectResult>(logoutAllAction);
        Assert.NotNull(logoutAllOk.Value);
    }
}
