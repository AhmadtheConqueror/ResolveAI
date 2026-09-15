import { useState } from "react";
import type { FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { login } from "../api/api";

export default function LoginPage() {
  const navigate = useNavigate();

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    setError("");
    setLoading(true);

    try {
      const response = await login(email, password);

      sessionStorage.setItem("accessToken", response.token);

      sessionStorage.setItem(
        "currentUser",
        JSON.stringify(response.user)
      );

      navigate("/dashboard");
    } catch (error) {
      setError(
        error instanceof Error
          ? error.message
          : "ResolveAI could not sign you in. Please try again."
      );
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="login-page">
      <div className="login-card">

        <div className="brand">
          <div className="brand-mark">R</div>

          <div>
            <h1>ResolveAI</h1>
            <p>Enterprise Incident Management</p>
          </div>
        </div>

        <div className="login-heading">
          <h2>Welcome back</h2>
          <p>Sign in to continue to ResolveAI.</p>
        </div>

        <form className="login-form" onSubmit={handleSubmit}>

          <label className="form-field">
            Email address

            <input
              type="email"
              value={email}
              onChange={(event) =>
                setEmail(event.target.value)
              }
              placeholder="you@company.com"
              required
            />
          </label>

          <label className="form-field">
            Password

            <input
              type="password"
              value={password}
              onChange={(event) =>
                setPassword(event.target.value)
              }
              placeholder="Enter your password"
              required
            />
          </label>

          {error && (
            <div className="error-message">
              {error}
            </div>
          )}

          <button
            type="submit"
            className="primary-button login-submit"
            disabled={loading}
          >
            {loading ? "Signing in..." : "Sign in"}
          </button>

        </form>

        <div className="login-footer">
          Secure access powered by ResolveAI
        </div>

      </div>
    </div>
  );
}
