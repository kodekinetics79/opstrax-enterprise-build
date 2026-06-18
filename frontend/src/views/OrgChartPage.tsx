'use client';

import { useCallback, useEffect, useState } from 'react';
import { ChevronDown, ChevronRight, RefreshCw, Search, Users } from 'lucide-react';
import { employeesApi } from '../api/employees';
import type { OrgChartNodeDto } from '../api/employees';
import { Avatar } from '../components/Avatar';

export function OrgChartPage() {
  const [tree, setTree] = useState<OrgChartNodeDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [collapsed, setCollapsed] = useState<Set<number>>(new Set());

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await employeesApi.orgChart(undefined, 8);
      setTree(data);
    } catch {
      setError('Failed to load org chart.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const toggle = (id: number) =>
    setCollapsed(prev => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });

  const matchesSearch = (node: OrgChartNodeDto): boolean => {
    if (!search) return true;
    const q = search.toLowerCase();
    return (
      node.fullName.toLowerCase().includes(q) ||
      node.employeeCode.toLowerCase().includes(q) ||
      node.designation.toLowerCase().includes(q) ||
      node.department.toLowerCase().includes(q) ||
      node.directReports.some(matchesSearch)
    );
  };

  return (
    <div className="p-6 max-w-5xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div className="flex items-center gap-3">
          <Users className="w-6 h-6 text-primary" />
          <div>
            <h1 className="text-xl font-semibold text-gray-900">Org Chart</h1>
            <p className="text-sm text-gray-500">Reporting hierarchy</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <div className="relative">
            <Search className="w-4 h-4 absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-400" />
            <input
              type="text"
              placeholder="Search people…"
              value={search}
              onChange={e => setSearch(e.target.value)}
              className="pl-8 pr-3 py-1.5 text-sm border border-gray-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary/30 w-52"
            />
          </div>
          <button
            type="button"
            onClick={load}
            className="flex items-center gap-1.5 text-sm text-gray-500 hover:text-gray-800 px-3 py-1.5 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
          >
            <RefreshCw className="w-3.5 h-3.5" />
            Refresh
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 text-red-700 rounded-lg px-4 py-3 text-sm mb-4">{error}</div>
      )}

      {loading ? (
        <div className="flex items-center justify-center py-24 text-gray-400 text-sm">Loading org chart…</div>
      ) : tree.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-24 text-gray-400">
          <Users className="w-10 h-10 mb-3 opacity-40" />
          <p className="text-sm">No employees found. Import employees to populate the org chart.</p>
        </div>
      ) : (
        <div className="space-y-1">
          {tree.filter(matchesSearch).map(node => (
            <OrgNode
              key={node.id}
              node={node}
              collapsed={collapsed}
              toggle={toggle}
              search={search}
              matchesSearch={matchesSearch}
            />
          ))}
        </div>
      )}
    </div>
  );
}

interface OrgNodeProps {
  node: OrgChartNodeDto;
  collapsed: Set<number>;
  toggle: (id: number) => void;
  search: string;
  matchesSearch: (n: OrgChartNodeDto) => boolean;
}

function OrgNode({ node, collapsed, toggle, search, matchesSearch }: OrgNodeProps) {
  const isCollapsed = collapsed.has(node.id);
  const hasReports = node.directReports.length > 0;
  const visibleReports = node.directReports.filter(matchesSearch);

  const highlight = (text: string) => {
    if (!search) return text;
    const idx = text.toLowerCase().indexOf(search.toLowerCase());
    if (idx === -1) return text;
    return (
      <>
        {text.slice(0, idx)}
        <mark className="bg-yellow-100 text-yellow-900 rounded">{text.slice(idx, idx + search.length)}</mark>
        {text.slice(idx + search.length)}
      </>
    );
  };

  return (
    <div>
      <div
        className="flex items-center gap-3 rounded-lg px-3 py-2 hover:bg-gray-50 cursor-pointer group transition-colors"
        onClick={() => hasReports && toggle(node.id)}
      >
        {/* Expand/collapse chevron */}
        <div className="w-5 flex-shrink-0">
          {hasReports ? (
            isCollapsed ? (
              <ChevronRight className="w-4 h-4 text-gray-400 group-hover:text-gray-600" />
            ) : (
              <ChevronDown className="w-4 h-4 text-gray-400 group-hover:text-gray-600" />
            )
          ) : (
            <span className="w-4 h-4 block" />
          )}
        </div>

        <Avatar name={node.fullName} size="sm" />

        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2">
            <span className="text-sm font-medium text-gray-900 truncate">
              {highlight(node.fullName)}
            </span>
            <span className="text-xs text-gray-400 flex-shrink-0">{highlight(node.employeeCode)}</span>
          </div>
          <div className="text-xs text-gray-500 truncate">
            {highlight(node.designation)}
            {node.department && (
              <> · <span className="text-gray-400">{highlight(node.department)}</span></>
            )}
          </div>
        </div>

        {hasReports && (
          <span className="text-xs text-gray-400 flex-shrink-0 bg-gray-100 rounded-full px-2 py-0.5">
            {node.directReports.length}
          </span>
        )}
      </div>

      {!isCollapsed && visibleReports.length > 0 && (
        <div className="pl-6">
          {visibleReports.map(child => (
            <OrgNode
              key={child.id}
              node={child}
              collapsed={collapsed}
              toggle={toggle}
              search={search}
              matchesSearch={matchesSearch}
            />
          ))}
        </div>
      )}
    </div>
  );
}
