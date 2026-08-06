const ADMIN_ROLE_NAME = 'admin'

export const isAdminRole = (roleName?: string) => (roleName ?? '').trim().toLowerCase() === ADMIN_ROLE_NAME
