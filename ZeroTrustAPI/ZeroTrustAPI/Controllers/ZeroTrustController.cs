using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ZeroTrustAPI.Models;
using ZeroTrustAPI.Services;
using static System.Collections.Specialized.BitVector32;

namespace ZeroTrustAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ZeroTrustController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        private int GetUserId()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(id))
                throw new UnauthorizedAccessException("User is not authenticated");

            return int.Parse(id);
        }

        public ZeroTrustController(IConfiguration config, AppDbContext db)
        {
            _config = config;
            _db = db;
        }

        [HttpGet("whoami")]                     // For debugging
        [Authorize]
        public IActionResult WhoAmI()
        {
            return Ok(new
            {
                Claims = User.Claims.Select(x => new { x.Type, x.Value })
            });
        }

        [HttpGet("how-many-active-sessions")]                     // For debugging
        public IActionResult HowManyActiveSessions()
        {
            return Ok(new
            {
                SessionStore.Sessions.Count
            });
        }

        [HttpGet("debug-token")]                // For debugging
        [Authorize]
        public IActionResult DebugToken()
        {
            return Ok(new
            {
                Claims = User.Claims.Select(c => new { c.Type, c.Value })
            });
        }

        [HttpGet("test")]
        public IActionResult Get()
        {
            return Ok("API is working!");
        }

        [Authorize]
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var isAuth = _authStatus.ContainsKey(userId) && _authStatus[userId];

            // If authenticated AND we have a pending token for this user
            if (isAuth && _pendingTokens.TryGetValue(userId, out var tokens))
            {
                // Remove it so it can only be fetched once for security
                _pendingTokens.Remove(userId);
                _authStatus[userId] = false; // Reset state

                return Ok(new
                {
                    Authenticated = true,
                    AccessToken = tokens.AccessToken,
                    RefreshToken = tokens.RefreshToken
                });
            }

            return Ok(new { Authenticated = false });
        }

        private static readonly Dictionary<int, (string Code, DateTime ExpiresAt)> _otpStore = new();
        private static readonly Dictionary<int, bool> _authStatus = new();
        private static readonly Dictionary<int, string> _refreshTokens = new();
        private static readonly Dictionary<int, TokenResponse> _pendingTokens = new();

        [Authorize]
        [HttpPost("generate")]
        public IActionResult GenerateOtp([FromBody] GenerateRequest request)
        {
            var userId = GetUserId();
            var swTotal = System.Diagnostics.Stopwatch.StartNew();  // Timing
            var swOtp = System.Diagnostics.Stopwatch.StartNew();    // Timing

            var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            swOtp.Stop();   // Timing
            _otpStore[userId] = (otp, DateTime.UtcNow.AddSeconds(10));

            Debug.WriteLine($"[PERF] OTP generation took {swOtp.ElapsedTicks} ticks ({swOtp.ElapsedMilliseconds} ms)"); // Timing

            System.Diagnostics.Debug.WriteLine(
            $"[OTP GENERATED] User: {userId}, OTP: {otp}, Expires: {DateTime.UtcNow.AddSeconds(10)}"
            );

            if (!SessionStore.Sessions.ContainsKey(userId))
            {
                SessionStore.Sessions[userId] = new SessionContext();
            }

            var session = SessionStore.Sessions[userId];

            session.UserId = userId;
            session.DeviceId = request.DeviceId;
            session.IpAddress = request.RealIp;
            session.Location = request.Location;

            session.OtpRequired = true;

            _authStatus[userId] = false;

            swTotal.Stop();
            Debug.WriteLine($"[PERF] TOTAL GenerateOtp took {swTotal.ElapsedMilliseconds} ms");

            return Ok(new
            {
                Message = "OTP Generated",
                Otp = otp,
                ExpiresAt = DateTime.UtcNow.AddSeconds(10)
            });
        }

        [HttpPost("verify")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyRequest request)
        {
            var userId = GetUserId();
            var swTotal = Stopwatch.StartNew();

            if (request == null || userId <= 0 || string.IsNullOrEmpty(request.Code))
                return BadRequest("Invalid request payload");

            var swValidation = Stopwatch.StartNew();

            bool isValid = _otpStore.TryGetValue(userId, out var storedOtp)
                && storedOtp.Code == request.Code
                && DateTime.UtcNow <= storedOtp.ExpiresAt;

            swValidation.Stop();
            Debug.WriteLine($"[PERF] OTP validation took {swValidation.ElapsedTicks} ticks ({swValidation.ElapsedMilliseconds} ms)");

            if (isValid)
            {
                _otpStore.Remove(userId);

                var swToken = Stopwatch.StartNew();
                var accessToken = GenerateAccessToken(userId.ToString());
                var refreshToken = GenerateRefreshToken();

                swToken.Stop();
                Debug.WriteLine($"[PERF] Token generation took {swToken.ElapsedMilliseconds} ms");

                _refreshTokens[userId] = refreshToken;

                // Store the token for the PC to pick up
                _pendingTokens[userId] = new TokenResponse
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken
                };

                if (!SessionStore.Sessions.ContainsKey(userId))
                {
                    return BadRequest(new { Success = false, Message = "Session not found. Start login again on PC." });
                }
                var session = SessionStore.Sessions[userId];

                _authStatus[userId] = true;

                var record = await _db.DeviceAccesses
                    .FirstOrDefaultAsync(x => x.UserId == userId);

                if (record != null)
                {
                    record.DeviceModel = session.DeviceId;
                    record.Location = session.Location;
                    record.IpAddress = session.IpAddress;
                    record.AccessTime = DateTime.UtcNow;

                    var swDb = Stopwatch.StartNew();
                    await _db.SaveChangesAsync();
                    swDb.Stop();

                    Debug.WriteLine($"[PERF] DB SaveChanges took {swDb.ElapsedMilliseconds} ms");
                }

                session.LastActivity = DateTime.UtcNow;
                session.OtpRequired = false;
                session.IsAfkExpired = false;
                session.IsCriticalPending = false;
                session.IpChanged = false;

                swTotal.Stop();
                Debug.WriteLine($"[PERF] TOTAL VerifyOtp took {swTotal.ElapsedMilliseconds} ms");

                return Ok(new { Success = true, Message = "Verified. Primary device can now proceed." });
            }
            else if (_otpStore.ContainsKey(userId))
            {
                _otpStore.Remove(userId); // Clean up expired entry
                return BadRequest(new { Success = false, Message = "OTP expired. A new one has been sent." });
            }

            return BadRequest(new { Success = false, Message = "Invalid OTP" });
        }

        [HttpPost("refresh")]
        public IActionResult Refresh([FromBody] RefreshRequest request)
        {
            // 1. Search by value: Find the entry where the value matches the incoming token
            var tokenEntry = _refreshTokens.FirstOrDefault(x => x.Value == request.RefreshToken);

            // 2. Check validity: If no match is found, the entry will be the default (empty)
            if (tokenEntry.Equals(default(KeyValuePair<int, string>)))
            {
                return Unauthorized(new { message = "Invalid or expired refresh token" });
            }

            // Now we have the User ID safely from the dictionary key
            int userId = tokenEntry.Key;

            // 3. Session maintenance: Update the session activity if it still exists
            if (SessionStore.Sessions.TryGetValue(userId, out var session))
            {
                session.LastActivity = DateTime.UtcNow;
            }

            // 4. Generate new tokens: Rotate both for better security
            var newAccessToken = GenerateAccessToken(userId.ToString());
            var newRefreshToken = GenerateRefreshToken();

            // 5. Update storage: Save the new refresh token for this user
            _refreshTokens[userId] = newRefreshToken;

            return Ok(new
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken
            });
        }

        [Authorize]
        [HttpPost("secure")]
        public async Task<IActionResult> SecureEndpoint([FromBody] DeviceRequest request)
        {
            var userId = GetUserId();
            var sw = Stopwatch.StartNew();

            if (userId <= 0)
                return BadRequest("Invalid user id");

            var currentIp = request.RealIp;
            var currentLocation = request.Location;
            var currentDevice = request.DeviceId;
            var action = request.Action;

            if (SessionStore.Sessions.TryGetValue(userId, out var session))
            {
                session.LastActivity = DateTime.UtcNow;

                // 1. Check if they were logged out completely
                if (session.IsAfkExpired)
                {
                    session.OtpRequired = true;
                    return Ok(new { otpRequired = true, message = "OTP required: Previous session ended due to AFK." });
                }
                else if (action.Equals("critical_action"))
                {
                    session.IsCriticalPending = true;
                    session.OtpRequired = true;
                    return Ok(new { otpRequired = true, message = "OTP required: Critical action." });
                }
                else if (session.IpChanged)
                {
                    session.IpChanged = true;
                    session.OtpRequired = true;
                    return Ok(new { otpRequired = true, message = "OTP required: IP changed." });
                }
                else if (session.OtpRequired)
                {
                    return Ok(new
                    {
                        otpRequired = true,
                        message = "OTP required (pending verification)."
                    });
                }
                
            }

            var record = await _db.DeviceAccesses
                .Include(x => x.User) 
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (record == null)         // If the user logs in for the absolute first time, we enter here
            {
                var newRecord = new DeviceAccess
                {
                    UserId = userId,
                    DeviceModel = currentDevice,
                    Location = currentLocation,
                    IpAddress = currentIp,
                    AccessTime = DateTime.UtcNow
                };
                _db.DeviceAccesses.Add(newRecord);
                await _db.SaveChangesAsync();

                if (!SessionStore.Sessions.ContainsKey(userId))
                {
                    SessionStore.Sessions[userId] = new SessionContext
                    {
                        UserId = userId,
                        Username = newRecord.User.Username,
                        IpAddress = currentIp,
                        Location = currentLocation,
                        DeviceId = currentDevice,
                        OtpRequired = false,
                        LastActivity = DateTime.UtcNow,
                        IsAfkExpired = false,
                        IsCriticalPending = false,
                        IpChanged = false
                    };
                }
                else
                {
                    // If it already existed (from the Login call), make sure it's cleared
                    session = SessionStore.Sessions[userId];
                    session.OtpRequired = false;
                    session.IsAfkExpired = false;
                    session.IsCriticalPending = false;
                    session.IpChanged = false;
                }

                return Ok(new { otpRequired = false, message = "Baseline created. Access granted." });
            }

            bool isIpMatch = record.IpAddress == currentIp;
            bool isLocationMatch = record.Location == currentLocation;
            bool isDeviceMatch = record.DeviceModel == currentDevice;

            // Get the time of day for both the current request and the last baseline access
            TimeSpan currentTimeOfDay = DateTime.UtcNow.TimeOfDay;
            TimeSpan lastLoginTimeOfDay = record.AccessTime.TimeOfDay;

            // Calculate the absolute difference in hours
            double hourDifference = Math.Abs((currentTimeOfDay - lastLoginTimeOfDay).TotalHours);

            // Handle the midnight wrap-around (for example, 23:00 and 02:00 are 3 hours apart, not 21 hours)
            if (hourDifference > 12)
            {
                hourDifference = 24 - hourDifference;
            }

            // True if the current time is within 4 hours (before or after) the last login's time of day
            bool isUsualHour = hourDifference <= 4;

            if (!isIpMatch || !isLocationMatch || !isDeviceMatch || !isUsualHour)
            {
                // FAIL: Flag the session so they are blocked until OTP is verified
                if (SessionStore.Sessions.TryGetValue(userId, out var s))
                {
                    s.OtpRequired = true;

                    if (!isIpMatch)
                    {
                        s.IpChanged = true;
                    }
                }

                string reason = "OTP required";

                if (!isIpMatch)
                    reason = "OTP required: IP changed";
                else if (!isLocationMatch)
                    reason = "OTP required: location changed";
                else if (!isDeviceMatch)
                    reason = "OTP required: device changed";
                else if (!isUsualHour)
                    reason = "OTP required: unusual hours";

                return Ok(new
                {
                    otpRequired = true,
                    message = reason
                });
            }

            record.AccessTime = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // ----------------------------------------------
            // Recreate the session
            if (!SessionStore.Sessions.ContainsKey(userId))
            {
                SessionStore.Sessions[userId] = new SessionContext
                {
                    UserId = userId,
                    Username = record.User?.Username ?? "Authenticated User",
                    IpAddress = currentIp,
                    Location = currentLocation,
                    DeviceId = currentDevice,
                    OtpRequired = false,
                    LastActivity = DateTime.UtcNow,
                    IsAfkExpired = false,
                    IsCriticalPending = false,
                    IpChanged = false
                };
                Debug.WriteLine($"[SESSION] Re-hydrated session for User {userId} after inactivity.");
            }
            // ----------------------------------------------

            sw.Stop();
            Debug.WriteLine($"[PERF] SecureEndpoint took {sw.ElapsedMilliseconds} ms");

            return Ok(new
            {
                otpRequired = false,
                message = "Access granted"
            });
        }

        [Authorize]
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            var userId = GetUserId();

            if (userId <= 0)
                return BadRequest();

            SessionStore.Sessions.Remove(userId);
            _refreshTokens.Remove(userId);
            _pendingTokens.Remove(userId);
            _authStatus.Remove(userId);
            _otpStore.Remove(userId);

            return Ok(new { message = "Logged out successfully" });
        }

        [HttpPost("timeout")]
        public IActionResult Timeout()
        {
            int userId = GetUserId();

            if (SessionStore.Sessions.TryGetValue(userId, out var session))
            {
                session.OtpRequired = true; // Force OTP on next attempt
                session.IsAfkExpired = true;
                return Ok();
            }
            return NotFound();
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (_db.Users.Any(u => u.Username == request.Username))
                return BadRequest("Username already exists");

            var user = new User
            {
                Username = request.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return Ok("User created");
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Unauthorized("Invalid credentials");

            // If a user session exists with these credentials, We don't create a new session here and instead process from the last one
            if (!SessionStore.Sessions.TryGetValue(user.Id, out var session))
            {
                // No session exists, so a new one is created
                session = new SessionContext
                {
                    UserId = user.Id
                };
                SessionStore.Sessions[user.Id] = session;
            }

            session.OtpRequired = false;
            session.LastActivity = DateTime.UtcNow;

            var accessToken = GenerateAccessToken(user.Id.ToString());

            if (request.IsMobile && _refreshTokens.TryGetValue(user.Id, out var existingRefreshToken))
            {
                return Ok(new
                {
                    AccessToken = accessToken,
                    RefreshToken = existingRefreshToken, // Reuse existing
                    UserId = user.Id
                });
            }

            var newRefreshToken = GenerateRefreshToken();
            _refreshTokens[user.Id] = newRefreshToken;

            return Ok(new
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                UserId = user.Id
            });
        }

        [Authorize]
        [HttpGet("session-status")]
        public IActionResult GetSessionStatus()
        {
            var userId = GetUserId();

            if (SessionStore.Sessions.TryGetValue(userId, out var session))
            {
                // If they are AFK or need OTP, they are NOT ready to enter the app
                if (session.IsAfkExpired || session.OtpRequired)
                {
                    return Ok(new { isReady = false, message = "Additional verification required." });
                }

                return Ok(new { isReady = true });
            }

            return Ok(new { isReady = false, message = "No active session found. Please login." });
        }

        private string GenerateAccessToken(string userId)
        {
            var sw = Stopwatch.StartNew();

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId)
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(10),
                signingCredentials: creds
            );

            var result = new JwtSecurityTokenHandler().WriteToken(token);

            sw.Stop();
            Debug.WriteLine($"[PERF] Access token generation took {sw.ElapsedMilliseconds} ms");

            return result;
        }

        private string GenerateRefreshToken()
        {
            return Guid.NewGuid().ToString();
        }
    }

    public class VerifyRequest
    {
        public string Code { get; set; }
    }

    public class TokenResponse
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
    }

    public class RefreshRequest
    {
        public string RefreshToken { get; set; }
    }

    public class ContextAuthRequest
    {
        public string DeviceId { get; set; }
        public string Location { get; set; }
        public string IpAddress { get; set; }
    }

    public class ContextAuthResponse
    {
        public bool RequiresOtp { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string Message { get; set; }
    }

    public class LoginRequest
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool IsMobile { get; set; }

    }

    public class RegisterRequest
    {
        public int userId { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}