using System.Security.Claims;

namespace Frimerki.Models.Extensions;

public static class ClaimsPrincipalExtensions {
    extension(ClaimsPrincipal that) {
        /// <summary>Email claim value, or null if not present.</summary>
        public string Email => that.FindFirst(ClaimTypes.Email)?.Value;

        /// <summary>NameIdentifier (user ID) claim value, or null if not present.</summary>
        public string UserId => that.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        /// <summary>Role claim value, or null if not present.</summary>
        public string Role => that.FindFirst(ClaimTypes.Role)?.Value;
    }
}

