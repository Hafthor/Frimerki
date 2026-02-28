using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;

namespace Frimerki.Models.Extensions;

public static class UserExtensions {
    extension(User that) {
        /// <summary>
        /// Full email address for this user (username@domain).
        /// </summary>
        public string Email => $"{that.Username}@{that.Domain.Name}";

        /// <summary>
        /// Maps this User entity to a UserSessionInfo DTO.
        /// </summary>
        public UserSessionInfo ToSessionInfo() => new() {
            Id = that.Id,
            Username = that.Username,
            Email = that.Email,
            FullName = that.FullName,
            Role = that.Role,
            CanReceive = that.CanReceive,
            CanLogin = that.CanLogin,
            DomainName = that.Domain.Name,
            DomainId = that.DomainId,
            LastLogin = that.LastLogin ?? DateTime.MinValue
        };
    }
}

