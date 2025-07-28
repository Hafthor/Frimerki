using Frimerki.Models.DTOs;

namespace Frimerki.Services.Message;

public interface IMessageService {
    Task<MessageListResponse> GetMessagesAsync(int userId, MessageFilterRequest request);
    Task<MessageResponse?> GetMessageAsync(int userId, int messageId);
    Task<MessageResponse> CreateMessageAsync(int userId, MessageRequest request);
    Task<MessageResponse?> UpdateMessageAsync(int userId, int messageId, MessageUpdateRequest request);
    Task<bool> DeleteMessageAsync(int userId, int messageId);
}
