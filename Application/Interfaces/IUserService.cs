using Application.DTOs.User;

namespace Application.Interfaces;

public interface IUserService
{
    Task<List<UserResponseDto>> GetAllUsersAsync();
    Task<UserResponseDto?> GetUserByIdAsync(int id);
    Task CreateUserAsync(UserCreateDto dto);
    Task UpdateOwnCredentialsAsync(int userId, UserUpdateDto dto);
    Task AdminUpdateUserAsync(
        int targetUserId,
        string newUsername,
        int? newDepartmentId,
        string? category = null,
        bool? isActive = null,
        bool? createSeparateTeams = null,
        string? teamAName = null,
        string? teamBName = null);
}
