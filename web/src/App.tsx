import { BrowserRouter, Route, Routes } from 'react-router'
import { AppShell } from './components/AppShell'
import { AuthProvider } from './components/AuthProvider'
import { RequireAuth } from './components/RequireAuth'
import { AccountsPage } from './pages/AccountsPage'
import { CategoriesPage } from './pages/CategoriesPage'
import { ChangePasswordPage } from './pages/ChangePasswordPage'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'
import { MembersPage } from './pages/MembersPage'
import { MonthlyOverviewPage } from './pages/MonthlyOverviewPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { RulesPage } from './pages/RulesPage'
import { SettingsPage } from './pages/SettingsPage'
import { TagReportPage } from './pages/TagReportPage'
import { TagsPage } from './pages/TagsPage'
import { TransactionsPage } from './pages/TransactionsPage'
import { UncategorisedPage } from './pages/UncategorisedPage'

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          {/* Everything else needs a logged-in Member and is shown inside the app shell. */}
          <Route element={<RequireAuth />}>
            <Route element={<AppShell />}>
              <Route index element={<HomePage />} />
              <Route path="overview" element={<MonthlyOverviewPage />} />
              <Route path="reports/tag" element={<TagReportPage />} />
              <Route path="reports/tag/:tagId" element={<TagReportPage />} />
              <Route path="transactions" element={<TransactionsPage />} />
              <Route path="uncategorised" element={<UncategorisedPage />} />
              <Route path="accounts" element={<AccountsPage />} />
              <Route path="settings/categories" element={<CategoriesPage />} />
              <Route path="settings/tags" element={<TagsPage />} />
              <Route path="settings/rules" element={<RulesPage />} />
              <Route path="account/password" element={<ChangePasswordPage />} />
              <Route path="settings" element={<SettingsPage />} />
              <Route path="settings/members" element={<MembersPage />} />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Route>
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  )
}
