"""
Frontend Developer Agent
=========================
Builds the React frontend: dark mode, mobile-first,
shadcn/ui, Zustand, React Query, WebView compatible.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUTPUT_DIR = os.path.join(WORKSPACE, "output")

SYSTEM_PROMPT = """
You are a Senior React Frontend Developer building ION CRM.

Tech stack:
- React 18 + TypeScript
- Vite (build tool)
- shadcn/ui (component library)
- Tailwind CSS
- Zustand (state management)
- React Query / TanStack Query (server state)
- React Router v6
- Recharts (charts/dashboards)
- Axios (HTTP client)
- React Hook Form + Zod (forms)

Design Requirements:
- Dark mode DEFAULT, light mode toggle
- Mobile-first responsive (must work in WebView for native app)
- Clean, professional CRM aesthetic
- Sidebar navigation
- Turkish language default
- Touch-friendly (44px min touch targets)

Color Palette (dark mode):
- Background: #0f1117
- Surface: #1a1d27
- Border: #2d3148
- Primary: #6366f1 (indigo)
- Success: #22c55e
- Warning: #f59e0b
- Danger: #ef4444
- Text primary: #f1f5f9
- Text muted: #64748b

App Structure:
frontend/
├── src/
│   ├── api/          (axios instances, query hooks)
│   ├── components/   (shadcn + custom)
│   ├── pages/        (route-level components)
│   ├── stores/       (Zustand stores)
│   ├── types/        (TypeScript interfaces)
│   ├── hooks/        (custom hooks)
│   ├── utils/        (helpers)
│   └── App.tsx

Pages to build:
- /login               (auth, remember me)
- /dashboard           (stats, recent activity, charts)
- /customers           (list, search, filter, pagination)
- /customers/:id       (detail, history, tasks, opportunities)
- /customers/new       (create form)
- /pipeline            (kanban board, drag-drop)
- /tasks               (task list, calendar view)
- /reports             (charts, export)
- /admin/users         (SuperAdmin: user management)
- /admin/projects      (SuperAdmin: project management)
- /settings            (profile, preferences)
- /sync/logs           (SuperAdmin: sync history)

Auth Rules in Frontend:
- JWT stored in memory (NOT localStorage) + refresh token in httpOnly cookie
- Axios interceptor auto-refreshes expired tokens
- Route guards based on role + project membership
- SuperAdmin sees project switcher
- Regular users see only their project's data

WebView Compatibility:
- No hover-only interactions
- Large tap targets
- No fixed positioning issues on mobile
- Test at 375px (iPhone SE) minimum

Always use TypeScript — no any types.
Use React Query for ALL API calls — no raw fetch/axios in components.
"""


class FrontendDevAgent(BaseAgent):
    name = "Frontend Developer"
    emoji = "🎨"
    color = "magenta"
    ALLOWED_TOOLS = [
        "Read", "Write", "Edit", "MultiEdit",
        "Glob", "Bash", "WebSearch"
    ]

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    async def scaffold_frontend(self) -> str:
        prompt = f"""
        Read CLAUDE.md at {WORKSPACE}/CLAUDE.md first.
        Read the API contracts in {OUTPUT_DIR}/docs/schema.md

        Scaffold the React frontend for ION CRM:

        cd {OUTPUT_DIR}

        1. Create Vite + React + TypeScript project:
           npm create vite@latest frontend -- --template react-ts
           cd frontend
           npm install

        2. Install dependencies:
           npm install tailwindcss postcss autoprefixer
           npm install @tanstack/react-query axios
           npm install zustand
           npm install react-router-dom
           npm install react-hook-form @hookform/resolvers zod
           npm install recharts
           npm install lucide-react
           npm install class-variance-authority clsx tailwind-merge
           npm install @radix-ui/react-dialog @radix-ui/react-dropdown-menu
           npm install @radix-ui/react-select @radix-ui/react-toast
           npm install @radix-ui/react-tooltip @radix-ui/react-avatar
           npm install date-fns

        3. Initialize Tailwind:
           npx tailwindcss init -p

        4. Setup shadcn/ui:
           npx shadcn@latest init
           (choose: TypeScript, Default style, Slate base color, yes CSS variables)
           
           Add components:
           npx shadcn@latest add button card input label
           npx shadcn@latest add dialog dropdown-menu select
           npx shadcn@latest add toast avatar badge
           npx shadcn@latest add table skeleton separator
           npx shadcn@latest add sheet sidebar

        5. Configure dark mode in tailwind.config.js:
           darkMode: 'class'

        6. Create the base structure:
           - src/api/client.ts (axios with interceptors)
           - src/stores/authStore.ts (Zustand auth state)
           - src/stores/themeStore.ts (dark/light mode)
           - src/types/index.ts (all TypeScript interfaces)
           - src/App.tsx (router setup)
           - src/main.tsx (providers setup)

        7. Create Login page (full implementation):
           - Beautiful dark-mode login form
           - Email + password
           - JWT handling
           - Remember me (refresh token)
           - Error handling
           - Loading states
           - Redirect to dashboard on success

        8. Create main layout:
           - Collapsible sidebar (mobile: drawer)
           - Top header with user menu, project switcher, theme toggle
           - Breadcrumbs
           - Notification bell

        9. Verify it builds:
           npm run build

        Fix any TypeScript errors.
        """
        return await self.run(prompt, OUTPUT_DIR)

    async def implement_customer_module(self) -> str:
        prompt = f"""
        Read CLAUDE.md at {WORKSPACE}/CLAUDE.md first.
        Read existing frontend code in {OUTPUT_DIR}/frontend/src/

        Implement the complete Customer module:

        1. src/api/customers.ts
           - React Query hooks: useCustomers, useCustomer, 
             useCreateCustomer, useUpdateCustomer, useDeleteCustomer
           - Search, filter, pagination support
           - Contact history queries

        2. src/pages/customers/CustomersPage.tsx
           - Data table with: search, filter by status/segment/assigned
           - Pagination
           - Quick actions (call, email, note)
           - Import/Export buttons
           - "New Customer" button → opens dialog

        3. src/pages/customers/CustomerDetailPage.tsx
           - Customer info card (editable inline)
           - Timeline: contact history (calls, emails, meetings, notes)
           - Tasks tab
           - Opportunities tab
           - "Add Note/Call/Email" buttons
           - Activity feed

        4. src/components/customers/
           - CustomerCard.tsx
           - AddContactHistoryDialog.tsx
           - CustomerStatusBadge.tsx
           - CustomerForm.tsx (create/edit)

        All components:
        - Dark mode compatible
        - Mobile responsive
        - Loading skeletons
        - Error states
        - Empty states with illustrations
        - Turkish labels

        Build: cd {OUTPUT_DIR}/frontend && npm run build
        """
        return await self.run(prompt, OUTPUT_DIR)
