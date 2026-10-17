using Application.DTOs.User;
using Application.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;

namespace Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IDepartmentRepository _departmentRepository;

    public UserService(IUserRepository userRepository, IDepartmentRepository departmentRepository)
    {
        _userRepository = userRepository;
        _departmentRepository = departmentRepository;
    }

    public async Task<List<UserResponseDto>> GetAllUsersAsync()
    {
        var users = await _userRepository.GetAllAsync();
        var stitchingDept = await _departmentRepository.GetByTypeAsync(DepartmentType.Stitching);

        // Ensure initial unlinked teams are directly connected to the first Stitching manager.
        if (stitchingDept != null)
        {
            var firstStitchingUser = users
                .Where(u => u.DepartmentId == stitchingDept.Id)
                .OrderBy(u => u.Id)
                .FirstOrDefault();

            var unlinkedTeams = stitchingDept.Teams.Where(t => t.IsActive && t.UserId == null).ToList();
            if (firstStitchingUser != null && unlinkedTeams.Count > 0)
            {
                foreach (var t in unlinkedTeams)
                    t.UserId = firstStitchingUser.Id;
                _departmentRepository.Update(stitchingDept);
                await _departmentRepository.SaveChangesAsync();
            }
        }

        return users.Select(u =>
        {
            var userTeams = stitchingDept?.Teams
                .Where(t => t.IsActive && t.UserId == u.Id)
                .OrderBy(t => t.Id)
                .Select(t => t.Name)
                .ToList() ?? new List<string>();

            return new UserResponseDto
            {
                Id = u.Id,
                Username = u.Username,
                Role = u.Role.ToString(),
                Category = u.Category.ToString(),
                DepartmentId = u.DepartmentId,
                DepartmentName = u.Department?.Name,
                DepartmentOrderIndex = u.Department?.OrderIndex,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                HasSeparateTeams = userTeams.Count > 0,
                TeamNames = userTeams
            };
        }).ToList();
    }

    public async Task<UserResponseDto?> GetUserByIdAsync(int id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null) return null;

        return new UserResponseDto
        {
            Id = user.Id,
            Username = user.Username,
            Role = user.Role.ToString(),
            Category = user.Category.ToString(),
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name,
            DepartmentOrderIndex = user.Department?.OrderIndex,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task CreateUserAsync(UserCreateDto dto)
    {
        var username = (dto.Username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            throw new InvalidOperationException("Username must be at least 3 characters.");

        if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 4)
            throw new InvalidOperationException("Password must be at least 4 characters.");

        if (await _userRepository.UsernameExistsAsync(username))
            throw new InvalidOperationException("This username is already taken.");

        if (!Enum.TryParse<UserRole>(dto.Role, true, out var role))
            role = UserRole.Manager;

        if (role == UserRole.Manager && !dto.DepartmentId.HasValue)
            throw new InvalidOperationException("Please select a department for the Manager user.");

        var category = Enum.TryParse<UserCategory>(dto.Category, true, out var parsedCat)
            ? parsedCat
            : UserCategory.Both;

        if (role == UserRole.Admin)
            category = UserCategory.Both;

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role = role,
            Category = category,
            DepartmentId = role == UserRole.Manager ? dto.DepartmentId : null,
            IsActive = true,
            CreatedAt = PakistanTime.Now
        };

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        // If this is a Stitching Manager and CreateSeparateTeams is selected, create 2 teams connected to this user.
        if (role == UserRole.Manager && dto.DepartmentId.HasValue && dto.CreateSeparateTeams)
        {
            var dept = await _departmentRepository.GetByIdAsync(dto.DepartmentId.Value);
            if (dept != null && dept.Type == DepartmentType.Stitching)
            {
                var tA = string.IsNullOrWhiteSpace(dto.TeamAName) ? $"{username} - Team A" : dto.TeamAName.Trim();
                var tB = string.IsNullOrWhiteSpace(dto.TeamBName) ? $"{username} - Team B" : dto.TeamBName.Trim();
                if (string.Equals(tA, tB, StringComparison.OrdinalIgnoreCase))
                    tB = tB + " 2";

                dept.Teams.Add(new StitchingTeam
                {
                    DepartmentId = dept.Id,
                    UserId = user.Id,
                    Name = tA.Length > 50 ? tA[..50] : tA,
                    IsActive = true
                });
                dept.Teams.Add(new StitchingTeam
                {
                    DepartmentId = dept.Id,
                    UserId = user.Id,
                    Name = tB.Length > 50 ? tB[..50] : tB,
                    IsActive = true
                });
                _departmentRepository.Update(dept);
                await _departmentRepository.SaveChangesAsync();
            }
        }
    }

    public async Task UpdateOwnCredentialsAsync(int userId, UserUpdateDto dto)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new InvalidOperationException("User not found");

        if (!string.IsNullOrWhiteSpace(dto.NewUsername) && dto.NewUsername != user.Username)
        {
            var exists = await _userRepository.UsernameExistsAsync(dto.NewUsername);
            if (exists)
                throw new InvalidOperationException("This username is already taken.");

            user.Username = dto.NewUsername;
        }

        if (!string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        }

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();
    }

    public async Task AdminUpdateUserAsync(
        int targetUserId,
        string newUsername,
        int? newDepartmentId,
        string? category = null,
        bool? isActive = null,
        bool? createSeparateTeams = null,
        string? teamAName = null,
        string? teamBName = null)
    {
        var user = await _userRepository.GetByIdAsync(targetUserId);
        if (user == null)
            throw new InvalidOperationException("User not found");

        if (!string.IsNullOrWhiteSpace(newUsername) && newUsername.Trim() != user.Username)
        {
            var trimmed = newUsername.Trim();
            var exists = await _userRepository.UsernameExistsAsync(trimmed);
            if (exists)
                throw new InvalidOperationException("This username is already taken.");
            user.Username = trimmed;
        }

        if (user.Role == UserRole.Manager && newDepartmentId.HasValue)
        {
            user.DepartmentId = newDepartmentId.Value;
        }
        else if (user.Role != UserRole.Manager)
        {
            user.DepartmentId = null;
        }

        if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<UserCategory>(category, true, out var parsedCat))
        {
            user.Category = user.Role == UserRole.Admin ? UserCategory.Both : parsedCat;
        }

        if (isActive.HasValue && user.Role != UserRole.Admin)
        {
            user.IsActive = isActive.Value;
        }

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync();

        if (user.Role == UserRole.Manager && user.DepartmentId.HasValue && createSeparateTeams.HasValue)
        {
            var dept = await _departmentRepository.GetByIdAsync(user.DepartmentId.Value);
            if (dept != null && dept.Type == DepartmentType.Stitching)
            {
                var existingUserTeams = dept.Teams.Where(t => t.UserId == user.Id).OrderBy(t => t.Id).ToList();
                if (createSeparateTeams.Value)
                {
                    if (existingUserTeams.Count >= 2)
                    {
                        existingUserTeams[0].IsActive = true;
                        existingUserTeams[1].IsActive = true;
                        if (!string.IsNullOrWhiteSpace(teamAName)) existingUserTeams[0].Name = teamAName.Trim();
                        if (!string.IsNullOrWhiteSpace(teamBName)) existingUserTeams[1].Name = teamBName.Trim();
                    }
                    else
                    {
                        var tA = string.IsNullOrWhiteSpace(teamAName) ? $"{user.Username} - Team A" : teamAName.Trim();
                        var tB = string.IsNullOrWhiteSpace(teamBName) ? $"{user.Username} - Team B" : teamBName.Trim();
                        if (string.Equals(tA, tB, StringComparison.OrdinalIgnoreCase))
                            tB = tB + " 2";

                        dept.Teams.Add(new StitchingTeam
                        {
                            DepartmentId = dept.Id,
                            UserId = user.Id,
                            Name = tA.Length > 50 ? tA[..50] : tA,
                            IsActive = true
                        });
                        dept.Teams.Add(new StitchingTeam
                        {
                            DepartmentId = dept.Id,
                            UserId = user.Id,
                            Name = tB.Length > 50 ? tB[..50] : tB,
                            IsActive = true
                        });
                    }
                    _departmentRepository.Update(dept);
                    await _departmentRepository.SaveChangesAsync();
                }
                else
                {
                    // Only deactivate if another active team pair exists in Stitching
                    var otherActiveTeams = dept.Teams.Count(t => t.IsActive && t.UserId != user.Id);
                    if (otherActiveTeams >= 2 && existingUserTeams.Count > 0)
                    {
                        foreach (var t in existingUserTeams)
                            t.IsActive = false;
                        _departmentRepository.Update(dept);
                        await _departmentRepository.SaveChangesAsync();
                    }
                }
            }
        }
    }
}
