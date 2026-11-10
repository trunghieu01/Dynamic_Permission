# Dynamic Permission database diagram

```mermaid
erDiagram
    ROLE ||--o{ USER : has
    USER ||--o{ REFRESH_TOKEN : owns
    USER ||--o{ USER_PERMISSION : overrides
    PERMISSION ||--o{ USER_PERMISSION : targets
    ROLE ||--o{ ROLE_PERMISSION : grants
    PERMISSION ||--o{ ROLE_PERMISSION : assigned

    ROLE {
        uuid Id PK
        string Code UK
        string Name
        bool IsActive
        datetime CreatedAt
    }

    USER {
        uuid Id PK
        uuid RoleId FK
        string Username UK
        string Email UK
        string PasswordHash
        string FullName
        bool IsActive
        datetime CreatedAt
    }

    PERMISSION {
        uuid Id PK
        string Code UK
        string Module
        string Action
        bool IsActive
        datetime CreatedAt
    }

    ROLE_PERMISSION {
        uuid RoleId PK, FK
        uuid PermissionId PK, FK
        datetime CreatedAt
    }

    USER_PERMISSION {
        uuid UserId PK, FK
        uuid PermissionId PK, FK
        bool IsGranted
        datetime CreatedAt
        datetime UpdatedAt
    }

    REFRESH_TOKEN {
        uuid Id PK
        uuid UserId FK
        string Token UK
        datetime ExpiresAt
        bool IsRevoked
        datetime CreatedAt
    }
```

## Permission evaluation

- Each `USER` has exactly one required `RoleId`; one `ROLE` can be assigned to many users.
- `ROLE_PERMISSION` is the many-to-many join between roles and permissions. Its composite key prevents duplicate assignment of the same permission to a role.
- `USER_PERMISSION` is a direct grant/deny override. Its composite key prevents duplicate override rows for the same user and permission.
- A user's effective permission set is derived from its active role's active permissions plus active direct grants, then direct denies are removed. Inactive users, roles, and permissions do not authorize requests.
- The current schema does not model many roles per user or a user without a role. Supporting either behavior would require an explicit domain/schema change and migration; these were not introduced as part of this test task.
