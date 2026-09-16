import {
  BrowserRouter,
  Navigate,
  Route,
  Routes,
} from "react-router-dom";

import LoginPage from "./pages/LoginPage";
import DashboardPage from "./pages/DashboardPage";
import IncidentsPage from "./pages/IncidentsPage";
import IncidentDetailsPage from "./pages/IncidentDetailsPage";
import MyWorkPage from "./pages/MyWorkPage";
import UsersPage from "./pages/UsersPage";

function App() {
  return (
    <BrowserRouter>

      <Routes>

        <Route
          path="/"
          element={<LoginPage />}
        />

        <Route
          path="/dashboard"
          element={<DashboardPage />}
        />

        <Route
          path="/incidents"
          element={<IncidentsPage />}
        />

        <Route
          path="/incidents/:id"
          element={<IncidentDetailsPage />}
        />

        <Route
          path="/my-work"
          element={<MyWorkPage />}
        />

        <Route
          path="/users"
          element={<UsersPage />}
        />

        <Route
          path="*"
          element={<Navigate to="/" />}
        />

      </Routes>

    </BrowserRouter>
  );
}

export default App;
