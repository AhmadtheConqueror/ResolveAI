import type { CSSProperties, ReactNode } from "react";

export type SkeletonVariant =
  | "text"
  | "heading"
  | "circle"
  | "rectangle"
  | "badge"
  | "button"
  | "card"
  | "pill";

export interface SkeletonProps {
  variant?: SkeletonVariant;
  width?: string | number;
  height?: string | number;
  borderRadius?: string | number;
  className?: string;
  style?: CSSProperties;
  "aria-hidden"?: boolean | "true" | "false";
}

/**
 * Base Skeleton component with support for multiple visual variants, custom dimensions,
 * shimmer animation, and accessible hiding.
 */
export function Skeleton({
  variant = "rectangle",
  width,
  height,
  borderRadius,
  className = "",
  style,
  "aria-hidden": ariaHidden = true,
}: SkeletonProps) {
  const variantClass = variant !== "rectangle" ? `skeleton-${variant}` : "";
  const combinedClass = ["skeleton-box", variantClass, className]
    .filter(Boolean)
    .join(" ");

  const combinedStyle: CSSProperties = {
    ...(width !== undefined ? { width } : {}),
    ...(height !== undefined ? { height } : {}),
    ...(borderRadius !== undefined ? { borderRadius } : {}),
    ...style,
  };

  return (
    <span
      className={combinedClass}
      style={combinedStyle}
      aria-hidden={ariaHidden}
    />
  );
}

export interface SkeletonTextProps {
  lines?: number;
  lastLineWidth?: string;
  lineHeight?: string | number;
  gap?: string | number;
  className?: string;
  style?: CSSProperties;
}

/**
 * Multi-line text placeholder with natural width variation on the final line.
 */
export function SkeletonText({
  lines = 3,
  lastLineWidth = "65%",
  lineHeight = 14,
  gap = 8,
  className = "",
  style,
}: SkeletonTextProps) {
  return (
    <div
      className={`skeleton-text-group ${className}`.trim()}
      style={{ gap, ...style }}
      aria-hidden="true"
    >
      {Array.from({ length: lines }).map((_, index) => {
        const isLast = index === lines - 1;
        return (
          <Skeleton
            key={index}
            variant="text"
            width={isLast ? lastLineWidth : "100%"}
            height={lineHeight}
          />
        );
      })}
    </div>
  );
}

export interface SkeletonCardProps {
  children?: ReactNode;
  className?: string;
  style?: CSSProperties;
}

/**
 * Standard card shell placeholder with aria-busy set for screen readers.
 */
export function SkeletonCard({
  children,
  className = "",
  style,
}: SkeletonCardProps) {
  return (
    <div
      className={`detail-card skeleton-card ${className}`.trim()}
      style={style}
      aria-busy="true"
      aria-live="polite"
    >
      <span className="sr-only">Loading content...</span>
      {children}
    </div>
  );
}

export interface SkeletonKpiCardProps {
  className?: string;
  style?: CSSProperties;
}

/**
 * Matches ResolveAI's .kpi-card layout (label, large metric, description).
 */
export function SkeletonKpiCard({
  className = "",
  style,
}: SkeletonKpiCardProps) {
  return (
    <div
      className={`kpi-card skeleton-kpi-card ${className}`.trim()}
      style={style}
      aria-busy="true"
      aria-live="polite"
    >
      <span className="sr-only">Loading metric...</span>
      <Skeleton width="48%" height={13} style={{ marginBottom: 12 }} />
      <Skeleton width="64%" height={34} style={{ marginBottom: 10 }} />
      <Skeleton width="76%" height={12} />
    </div>
  );
}

export interface SkeletonTableCellConfig {
  width?: string | number;
  height?: string | number;
  variant?: SkeletonVariant;
  align?: "left" | "center" | "right";
  className?: string;
}

export interface SkeletonTableRowProps {
  columns: (SkeletonTableCellConfig | string | number)[];
  className?: string;
}

/**
 * Table row placeholder rendering multiple columns with customized widths and shapes.
 */
export function SkeletonTableRow({
  columns,
  className = "",
}: SkeletonTableRowProps) {
  return (
    <tr className={`skeleton-table-row ${className}`.trim()} aria-hidden="true">
      {columns.map((col, index) => {
        const isConfig = typeof col === "object" && col !== null;
        const width = isConfig ? col.width : col;
        const height = isConfig ? col.height ?? 16 : 16;
        const variant = isConfig ? col.variant ?? "text" : "text";
        const align = isConfig ? col.align ?? "left" : "left";

        return (
          <td
            key={index}
            className={`skeleton-table-td ${isConfig && col.className ? col.className : ""}`.trim()}
            style={{ textAlign: align }}
          >
            <Skeleton
              variant={variant}
              width={width ?? "80%"}
              height={height}
              style={{
                display: align === "center" ? "inline-block" : "block",
                margin: align === "center" ? "0 auto" : undefined,
              }}
            />
          </td>
        );
      })}
    </tr>
  );
}
